#!/bin/bash
# ==========================================================
# Prowlarr Health Manager v0.5.1
# Auto mode + backup + restore + optional apply
#
# Default: DRY-RUN
#   - testa Prowlarr
#   - testa gli indexer
#   - scrive report/storico/log
#   - valuta eventuali azioni, ma NON modifica nulla
#
# Apply:
#   script --apply
#   - crea backup degli indexer
#   - prova correzione baseUrl se disponibili mirror alternativi
#   - se fallisce oltre soglia, disabilita indexer
#
# Restore:
#   script --restore /boot/config/plugins/user.scripts/prowlarr-health/backups/prowlarr-indexers-YYYYMMDD-HHMMSS.json
#
# Container reale: prowlarr
# ==========================================================

set -euo pipefail

CONTAINER="prowlarr"
PROWLARR_URL="http://127.0.0.1:9696"

BASE_DIR="/boot/config/plugins/user.scripts/prowlarr-health"
CONFIG_FILE="$BASE_DIR/config.conf"
LOG_DIR="$BASE_DIR/logs"
BACKUP_DIR="$BASE_DIR/backups"
REPORT_FILE="$BASE_DIR/last_report.json"
HISTORY_FILE="$BASE_DIR/history.jsonl"

FAIL_THRESHOLD=3
APPLY=false
RESTORE_FILE=""

mkdir -p "$BASE_DIR" "$LOG_DIR" "$BACKUP_DIR"

while [[ $# -gt 0 ]]; do
  case "${1:-}" in
    --apply|apply)
      APPLY=true
      shift
      ;;
    --restore|restore)
      if [[ -z "${2:-}" ]]; then
        echo "[FAIL] Devi indicare il file di backup da ripristinare"
        exit 1
      fi
      RESTORE_FILE="$2"
      shift 2
      ;;
    --help|-h|help)
      cat <<EOF
Prowlarr Health Manager v0.5.1

Uso:
  script
      Dry-run: nessuna modifica.

  script --apply
      Applica modifiche automatiche dopo backup.

  script --restore /percorso/backup.json
      Ripristina configurazione indexer da backup.

Percorsi:
  Config : $CONFIG_FILE
  Report : $REPORT_FILE
  Storico: $HISTORY_FILE
  Backup : $BACKUP_DIR
EOF
      exit 0
      ;;
    ""|" ")
      shift
      ;;
    *)
      echo "[WARN] Parametro ignorato: $1"
      shift
      ;;
  esac
done

ok(){ echo "[ OK ] $1"; }
warn(){ echo "[WARN] $1"; }
fail(){ echo "[FAIL] $1"; exit 1; }
line(){ echo "------------------------------------------------------------"; }

log(){
  echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*" >> "$LOG_DIR/phm.log"
}

require_cmd(){
  command -v "$1" >/dev/null || fail "$1 non trovato"
}

api_get(){
  curl -sS \
    -H "X-Api-Key: $API_KEY" \
    "$PROWLARR_URL$1"
}

api_put_json(){
  curl -sS -X PUT \
    -H "X-Api-Key: $API_KEY" \
    -H "Content-Type: application/json" \
    --data "$2" \
    "$PROWLARR_URL$1"
}

api_post_json(){
  curl -sS -X POST \
    -H "X-Api-Key: $API_KEY" \
    -H "Content-Type: application/json" \
    --data "$2" \
    "$PROWLARR_URL$1"
}

api_http_code(){
  curl -s \
    -o /dev/null \
    -w "%{http_code}" \
    -H "X-Api-Key: $API_KEY" \
    "$PROWLARR_URL$1"
}

read_api_key_from_container(){
  API_KEY=$(docker exec "$CONTAINER" sh -c \
    "sed -n 's:.*<ApiKey>\(.*\)</ApiKey>.*:\1:p' /config/config.xml" \
    | tr -d '\r\n')

  [[ -z "${API_KEY:-}" ]] && fail "API Key non trovata in /config/config.xml"
}

save_config(){
  cat > "$CONFIG_FILE" <<EOF
PROWLARR_URL="$PROWLARR_URL"
API_KEY="$API_KEY"
FAIL_THRESHOLD="$FAIL_THRESHOLD"
EOF
  chmod 600 "$CONFIG_FILE"
}

load_config(){
  if [[ -f "$CONFIG_FILE" ]]; then
    # shellcheck disable=SC1090
    source "$CONFIG_FILE"
    PROWLARR_URL="${PROWLARR_URL:-http://127.0.0.1:9696}"
    FAIL_THRESHOLD="${FAIL_THRESHOLD:-3}"
  fi

  if [[ -z "${API_KEY:-}" ]]; then
    read_api_key_from_container
    save_config
  fi
}

check_api(){
  local http
  http=$(api_http_code "/api/v1/system/status")

  if [[ "$http" == "401" ]]; then
    warn "API Key salvata non valida, rileggo dal container"
    read_api_key_from_container
    save_config
    http=$(api_http_code "/api/v1/system/status")
  fi

  [[ "$http" != "200" ]] && fail "API Prowlarr non raggiungibile. HTTP $http"
}

backup_indexers(){
  local ts file
  ts=$(date '+%Y%m%d-%H%M%S')
  file="$BACKUP_DIR/prowlarr-indexers-$ts.json"

  api_get "/api/v1/indexer" | jq '.' > "$file"
  chmod 600 "$file"

  echo "$file"
}

restore_backup(){
  [[ ! -f "$RESTORE_FILE" ]] && fail "Backup non trovato: $RESTORE_FILE"

  line
  echo "Ripristino da:"
  echo "$RESTORE_FILE"
  line

  while read -r IDX; do
    ID=$(echo "$IDX" | jq -r '.id')
    NAME=$(echo "$IDX" | jq -r '.name // "unknown"')

    echo "Ripristino indexer $ID - $NAME"
    api_put_json "/api/v1/indexer/$ID" "$IDX" >/dev/null

  done < <(jq -c '.[]' "$RESTORE_FILE")

  log "Restore completato file=$RESTORE_FILE"
  ok "Ripristino completato"
  exit 0
}

extract_error(){
  jq -r '
    if type=="array" then
      map(
        if type=="object" then
          (.errorMessage // .message // .propertyName // tostring)
        else
          tostring
        end
      ) | join(" | ")
    elif type=="object" then
      (.errorMessage // .message // tostring)
    else
      tostring
    end
  ' 2>/dev/null
}

test_indexer(){
  local idx="$1"
  local start end latency response result error

  start=$(date +%s%3N)
  response=$(api_post_json "/api/v1/indexer/test" "$idx" 2>/dev/null || true)
  end=$(date +%s%3N)

  latency=$((end - start))

  if echo "$response" | jq -e '. == {} or .result == "success" or .successful == true' >/dev/null 2>&1; then
    result="OK"
    error=""
  else
    result="FAIL"
    error=$(echo "$response" | extract_error || true)
    [[ -z "$error" || "$error" == "null" ]] && error="$response"
  fi

  jq -n \
    --arg result "$result" \
    --arg error "$error" \
    --argjson latency "$latency" \
    '{result:$result,error:$error,latency_ms:$latency}'
}

fail_count(){
  local id="$1"

  [[ ! -f "$HISTORY_FILE" ]] && echo 0 && return

  tail -n 200 "$HISTORY_FILE" | jq -s --argjson id "$id" '
    map(select(.id == $id)) |
    reverse |
    reduce .[] as $x (
      {count:0,stop:false};
      if .stop then .
      elif $x.result == "FAIL" then .count += 1
      else .stop = true
      end
    ) | .count
  '
}

candidate_baseurls(){
  local idx="$1"

  echo "$idx" | jq -r '
    .fields[]? |
    select(.name=="baseUrl") |
    (
      .selectOptions[]?.value,
      .selectOptions[]?.name,
      .value
    )
  ' | grep -E '^https?://' | sort -u || true
}

set_baseurl(){
  local idx="$1"
  local url="$2"

  echo "$idx" | jq --arg url "$url" '
    .fields |= map(
      if .name == "baseUrl" then
        .value = $url
      else
        .
      end
    )
  '
}

try_fix_baseurl(){
  local idx="$1"
  local id name current candidates url modified test result

  id=$(echo "$idx" | jq -r '.id')
  name=$(echo "$idx" | jq -r '.name')
  current=$(echo "$idx" | jq -r '.fields[]? | select(.name=="baseUrl") | .value' | head -n1)

  candidates=$(candidate_baseurls "$idx")

  [[ -z "$candidates" ]] && return 1

  while read -r url; do
    [[ -z "$url" ]] && continue
    [[ "$url" == "$current" ]] && continue

    echo "Provo mirror alternativo per $name: $url"

    modified=$(set_baseurl "$idx" "$url")
    test=$(test_indexer "$modified")
    result=$(echo "$test" | jq -r '.result')

    if [[ "$result" == "OK" ]]; then
      if [[ "$APPLY" == "true" ]]; then
        api_put_json "/api/v1/indexer/$id" "$modified" >/dev/null
        echo "AZIONE: aggiornato $name -> $url"
        log "Aggiornato baseUrl indexer=$name id=$id url=$url"
      else
        echo "DRY-RUN: aggiornerei $name -> $url"
      fi
      return 0
    fi

  done <<< "$candidates"

  return 1
}

disable_indexer(){
  local idx="$1"
  local id name modified

  id=$(echo "$idx" | jq -r '.id')
  name=$(echo "$idx" | jq -r '.name')
  modified=$(echo "$idx" | jq '.enable=false')

  if [[ "$APPLY" == "true" ]]; then
    api_put_json "/api/v1/indexer/$id" "$modified" >/dev/null
    echo "AZIONE: disabilitato $name"
    log "Disabilitato indexer=$name id=$id"
  else
    echo "DRY-RUN: disabiliterei $name"
  fi
}

main(){
  line
  echo "Prowlarr Health Manager v0.5.1"
  [[ "$APPLY" == "true" ]] && echo "MODE: APPLY" || echo "MODE: DRY-RUN"
  line

  require_cmd docker
  require_cmd curl
  require_cmd jq

  docker ps --format '{{.Names}}' | grep -qx "$CONTAINER" \
    || fail "Container $CONTAINER non in esecuzione"

  ok "Docker OK"
  ok "Container $CONTAINER OK"

  load_config
  check_api

  if [[ -n "$RESTORE_FILE" ]]; then
    restore_backup
  fi

  BACKUP_FILE=""
  if [[ "$APPLY" == "true" ]]; then
    BACKUP_FILE=$(backup_indexers)
    ok "Backup creato: $BACKUP_FILE"
  fi

  STATUS=$(api_get "/api/v1/system/status")
  VERSION=$(echo "$STATUS" | jq -r '.version // "unknown"')
  INSTANCE=$(echo "$STATUS" | jq -r '.instanceName // "Prowlarr"')

  echo "Instance : $INSTANCE"
  echo "Versione : $VERSION"

  INDEXERS=$(api_get "/api/v1/indexer")
  COUNT=$(echo "$INDEXERS" | jq length)

  TMP_REPORT=$(mktemp)
  echo "[]" > "$TMP_REPORT"

  OK_COUNT=0
  FAIL_COUNT=0
  DISABLED_COUNT=0
  ACTION_COUNT=0

  line
  printf "%-5s %-30s %-8s %-10s %-8s %-8s %s\n" "ID" "NAME" "ENABLED" "RESULT" "FAILS" "MS" "ERROR"
  line

  while read -r IDX; do
    ID=$(echo "$IDX" | jq -r '.id')
    NAME=$(echo "$IDX" | jq -r '.name')
    ENABLED=$(echo "$IDX" | jq -r '.enable')
    IMPLEMENTATION=$(echo "$IDX" | jq -r '.implementation // "unknown"')
    PROTOCOL=$(echo "$IDX" | jq -r '.protocol // "unknown"')

    ACTION=""

    if [[ "$ENABLED" != "true" ]]; then
      RESULT="DISABLED"
      ERROR=""
      LATENCY=0
      FAILS=0
      DISABLED_COUNT=$((DISABLED_COUNT + 1))
    else
      TEST=$(test_indexer "$IDX")
      RESULT=$(echo "$TEST" | jq -r '.result')
      ERROR=$(echo "$TEST" | jq -r '.error')
      LATENCY=$(echo "$TEST" | jq -r '.latency_ms')

      if [[ "$RESULT" == "OK" ]]; then
        OK_COUNT=$((OK_COUNT + 1))
        FAILS=0
      else
        FAIL_COUNT=$((FAIL_COUNT + 1))
        PREVIOUS_FAILS=$(fail_count "$ID")
        FAILS=$((PREVIOUS_FAILS + 1))

        if (( FAILS >= FAIL_THRESHOLD )); then
          if try_fix_baseurl "$IDX"; then
            ACTION="baseUrl_update_attempted"
            ACTION_COUNT=$((ACTION_COUNT + 1))
          else
            disable_indexer "$IDX"
            ACTION="disable_attempted"
            ACTION_COUNT=$((ACTION_COUNT + 1))
          fi
        fi
      fi
    fi

    SHORT_ERROR=$(echo "$ERROR" | cut -c1-80)

    printf "%-5s %-30s %-8s %-10s %-8s %-8s %s\n" \
      "$ID" "$NAME" "$ENABLED" "$RESULT" "$FAILS" "$LATENCY" "$SHORT_ERROR"

    ENTRY=$(jq -n \
      --arg timestamp "$(date -Iseconds)" \
      --argjson id "$ID" \
      --arg name "$NAME" \
      --arg enabled "$ENABLED" \
      --arg implementation "$IMPLEMENTATION" \
      --arg protocol "$PROTOCOL" \
      --arg result "$RESULT" \
      --arg error "$ERROR" \
      --arg action "$ACTION" \
      --argjson latency "$LATENCY" \
      --argjson fails "$FAILS" \
      '{
        timestamp:$timestamp,
        id:$id,
        name:$name,
        enabled:($enabled=="true"),
        implementation:$implementation,
        protocol:$protocol,
        result:$result,
        error:$error,
        latency_ms:$latency,
        consecutive_failures:$fails,
        action:$action
      }')

    echo "$ENTRY" >> "$HISTORY_FILE"

    jq --argjson entry "$ENTRY" '. + [$entry]' "$TMP_REPORT" > "$TMP_REPORT.new"
    mv "$TMP_REPORT.new" "$TMP_REPORT"

  done < <(echo "$INDEXERS" | jq -c '.[]')

  mv "$TMP_REPORT" "$REPORT_FILE"

  line
  echo "Totale indexer : $COUNT"
  echo "OK             : $OK_COUNT"
  echo "Fail           : $FAIL_COUNT"
  echo "Disabilitati   : $DISABLED_COUNT"
  echo "Azioni         : $ACTION_COUNT"
  echo "Report         : $REPORT_FILE"
  echo "Storico        : $HISTORY_FILE"
  echo "Log            : $LOG_DIR/phm.log"
  [[ -n "$BACKUP_FILE" ]] && echo "Backup         : $BACKUP_FILE"
  line

  log "Run completato mode=$([[ "$APPLY" == "true" ]] && echo apply || echo dry-run) total=$COUNT ok=$OK_COUNT fail=$FAIL_COUNT disabled=$DISABLED_COUNT actions=$ACTION_COUNT"

  ok "Completato"
}

main
