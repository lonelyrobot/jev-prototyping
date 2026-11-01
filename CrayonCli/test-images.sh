#!/usr/bin/env bash
# Runs every test-files/<color>.jpg through crayon and checks that Jev picks <color>.
# Usage:  ./test-images.sh
# Makes one TypeSafe API call per image.
# Exit codes: 0 all passed, 1 at least one wrong pick or error, 2 build failed.

set -u
cd "$(dirname "$0")" # crayon looks for .env starting from the current directory

if ! build_output=$(dotnet build --nologo -v quiet 2>&1); then
    echo "$build_output" >&2
    exit 2
fi

passed=0
failed=0

for image in test-files/*.jpg; do
    [ -e "$image" ] || continue
    expected=$(basename "$image" .jpg)

    if output=$(dotnet run --no-build -- "$image" 2>&1); then
        summary=$(echo "$output" | grep '^Closest crayon:')
        actual=$(echo "$summary" | awk '{print $3}')
        if [ "$actual" = "$expected" ]; then
            result=PASS
        else
            result=FAIL
        fi
        detail=${summary#Closest crayon: }
    else
        result=ERROR
        detail=$(echo "$output" | tail -n 1)
    fi

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

if [ "$total" -eq 0 ]; then
    echo "No images found in test-files/." >&2
    exit 1
fi

[ "$failed" -eq 0 ]
