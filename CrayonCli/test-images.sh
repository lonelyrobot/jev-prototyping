#!/usr/bin/env bash
# Starts the API, uploads every test-files/<color>.jpg and checks that Jev picks <color>.
# Usage:  ./test-images.sh
# Requires curl and jq. Makes one TypeSafe API call per image.
# Exit codes: 0 all passed, 1 at least one wrong pick or error, 2 build or server startup failed.

set -u
cd "$(dirname "$0")" # the API looks for .env starting from the current directory

if ! build_output=$(dotnet build --nologo -v quiet 2>&1); then
    echo "$build_output" >&2
    exit 2
fi

port=${CRAYON_TEST_PORT:-5099}
url="http://127.0.0.1:$port"
server_log=$(mktemp)

dotnet run --no-build --no-launch-profile --urls "$url" >"$server_log" 2>&1 &
server_pid=$!

cleanup() {
    kill "$server_pid" 2>/dev/null
    wait "$server_pid" 2>/dev/null
    rm -f "$server_log"
}
trap cleanup EXIT

# Any HTTP response means the server is up. GET on this POST-only route returns 405.
ready=false
for _ in $(seq 1 60); do
    if ! kill -0 "$server_pid" 2>/dev/null; then
        break
    fi
    if curl -s -o /dev/null "$url/crayon"; then
        ready=true
        break
    fi
    sleep 0.5
done

if [ "$ready" != true ]; then
    echo "Server didn't start:" >&2
    cat "$server_log" >&2
    exit 2
fi

passed=0
failed=0

for image in test-files/*.jpg; do
    [ -e "$image" ] || continue
    expected=$(basename "$image" .jpg)

    body=$(mktemp)
    status=$(curl -s -o "$body" -w '%{http_code}' -F "image=@$image" "$url/crayon")

    if [ "$status" = 200 ]; then
        actual=$(jq -r .closest "$body")
        if [ "$actual" = "$expected" ]; then
            result=PASS
        else
            result=FAIL
        fi
        detail=$(jq -r '"\(.closest)  (confidence \(.confidence * 100 | round)%)"' "$body")
    else
        result=ERROR
        detail="HTTP $status: $(jq -r '.detail // empty' "$body" 2>/dev/null)"
    fi
    rm -f "$body"

    printf '%-5s  %-7s  %s\n' "$result" "$expected" "$detail"

    if [ "$result" = PASS ]; then
        passed=$((passed + 1))
    else
        failed=$((failed + 1))
    fi
done

total=$((passed + failed))
echo
echo "$passed/$total passed"

if grep -q '^fail:' "$server_log"; then
    echo
    echo "Server errors:" >&2
    grep -A 3 '^fail:' "$server_log" | cut -c1-400 >&2
fi

if [ "$total" -eq 0 ]; then
    echo "No images found in test-files/." >&2
    exit 1
fi

[ "$failed" -eq 0 ]
