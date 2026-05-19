#!/bin/bash
# Usage: ./bin/run_tests.sh

# Heads up - the TestRamXXX tests seem to be flaky when run from this script, sometimes fail, but they seem fine when run from editor, not sure why 

SCRIPT_DIR="$( cd "$( dirname "${BASH_SOURCE[0]}" )" && pwd )"

echo "=== Running All Tests ==="

bash "$SCRIPT_DIR/run_editmode_tests.sh"
EDITMODE_EXIT=$?

bash "$SCRIPT_DIR/run_playmode_tests.sh"
PLAYMODE_EXIT=$?

bash "$SCRIPT_DIR/run_standalone_tests.sh"
STANDALONE_EXIT=$?

echo ""
echo "=== Test Summary ==="

FAILED=0

if [ $EDITMODE_EXIT -eq 0 ]; then
    echo "✅ EditMode tests passed."
else
    echo "❌ EditMode tests failed."
    FAILED=1
fi

if [ $PLAYMODE_EXIT -eq 0 ]; then
    echo "✅ PlayMode tests passed."
else
    echo "❌ PlayMode tests failed."
    FAILED=1
fi

if [ $STANDALONE_EXIT -eq 0 ]; then
    echo "✅ Standalone tests passed."
else
    echo "❌ Standalone tests failed."
    FAILED=1
fi

echo "===================="

if [ $FAILED -ne 0 ]; then
    # echo "❌ Some sets of tests failed."
    exit 1
else
    echo "✅ All tests passed successfully."
    exit 0
fi

