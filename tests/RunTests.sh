#!/usr/bin/env bash
#
# Run csedlint against fixture files and verify expected findings.
# Usage: bash tests/RunTests.sh
#
# Requires csedlint to be installed:
#   dotnet tool install --global csedlint

set -euo pipefail

PASS=0
FAIL=0
SKIP=0

check() {
    local name="$1"
    local expected_rule="$2"
    shift 2
    local output
    output=$(csedlint "$@" 2>&1 || true)

    if echo "$output" | grep -q "$expected_rule"; then
        echo "  PASS  $name"
        ((PASS++))
    else
        echo "  FAIL  $name  (expected rule $expected_rule not found)"
        echo "        output: $output"
        ((FAIL++))
    fi
}

check_absent() {
    local name="$1"
    local unexpected_rule="$2"
    shift 2
    local output
    output=$(csedlint "$@" 2>&1 || true)

    if echo "$output" | grep -q "$unexpected_rule"; then
        echo "  FAIL  $name  (unexpected rule $unexpected_rule found)"
        ((FAIL++))
    else
        echo "  PASS  $name (clean)"
        ((PASS++))
    fi
}

FIXTURE_DIR="$(cd "$(dirname "$0")"/fixtures && pwd)"

echo
echo "=== EditorConfig rules ==="
check "EC001 indent tabs"    "EC001" "$FIXTURE_DIR/editorconfig/EC001_IndentTabs.cs"
check "EC002 trailing ws"    "EC002" "$FIXTURE_DIR/editorconfig/EC002_TrailingWhitespace.cs"
check "EC003 no final nl"    "EC003" "$FIXTURE_DIR/editorconfig/EC003_NoFinalNewline.cs"
check "EC005 line length"    "EC005" "$FIXTURE_DIR/editorconfig/EC005_LineLength.cs"
check "CS010 var style"      "CS010" "$FIXTURE_DIR/editorconfig/CS010_VarStyle.cs"
check "CS011 expr bodies"    "CS011" "$FIXTURE_DIR/editorconfig/CS011_ExpressionBodies.cs"
check "CS020 namespace"      "CS020" "$FIXTURE_DIR/editorconfig/CS020_NamespaceDeclaration.cs"
check "CS032 accessibility" "CS032" "$FIXTURE_DIR/editorconfig/CS032_AccessibilityModifiers.cs"
check "CS033 readonly"       "CS033" "$FIXTURE_DIR/editorconfig/CS033_ReadonlyField.cs"
check "CS040 naming"         "CS040" "$FIXTURE_DIR/editorconfig/CS040_Naming.cs"

echo
echo "=== SAST rules ==="
check "SAST001 empty catch"    "SAST001" --sast "$FIXTURE_DIR/sast/SAST001_EmptyCatch.cs"
check "SAST002 console output" "SAST002" --sast "$FIXTURE_DIR/sast/SAST002_ConsoleOutput.cs"
check "SAST003 SQL injection"  "SAST003" --sast "$FIXTURE_DIR/sast/SAST003_SqlInjection.cs"
check "SAST004 secrets"        "SAST004" --sast "$FIXTURE_DIR/sast/SAST004_HardcodedSecrets.cs"
check "SAST005 fire-forget"    "SAST005" --sast "$FIXTURE_DIR/sast/SAST005_FireAndForget.cs"
check "SAST006 pragma"         "SAST006" --sast "$FIXTURE_DIR/sast/SAST006_PragmaSuppress.cs"
check "SAST007 thread sleep"   "SAST007" --sast "$FIXTURE_DIR/sast/SAST007_ThreadSleep.cs"
check "SAST008 dynamic"        "SAST008" --sast "$FIXTURE_DIR/sast/SAST008_DynamicUsage.cs"

echo
echo "=== Opinionated scan ==="
check "OP001 god function"    "OP001" --scan "$FIXTURE_DIR/opinionated/OP001_GodFunction.cs"
check "OP002 deep nesting"    "OP002" --scan "$FIXTURE_DIR/opinionated/OP002_DeepNesting.cs"
check "OP003 long params"     "OP003" --scan "$FIXTURE_DIR/opinionated/OP003_LongParams.cs"
check "OP004 magic numbers"   "OP004" --scan "$FIXTURE_DIR/opinionated/OP004_MagicNumbers.cs"
check "OP005 bool flags"      "OP005" --scan "$FIXTURE_DIR/opinionated/OP005_BooleanFlags.cs"
check "OP006 cancel token"    "OP006" --scan "$FIXTURE_DIR/opinionated/OP006_MissingCancellationToken.cs"

echo
echo "=== Results ==="
echo "  Passed: $PASS"
echo "  Failed: $FAIL"
echo

if [ $FAIL -gt 0 ]; then
    exit 1
fi
