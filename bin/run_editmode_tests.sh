#!/bin/bash
# Usage: ./bin/run_editmode_tests.sh

: "${UNITY_VERSION:=$(grep '^m_EditorVersion:' ProjectSettings/ProjectVersion.txt | awk '{print $2}')}"
: "${UNITY_EXE:=/c/Program Files/Unity/Hub/Editor/${UNITY_VERSION}/Editor/Unity.exe}"

cmd="\"${UNITY_EXE}\"
  -runTests \
  -batchmode \
  $@ \
  -projectPath ."

echo "Running EditMode tests..."

mkdir -p artifacts
> artifacts/test_log_editmode.txt
tail -f artifacts/test_log_editmode.txt | grep "\[unity-hawk\] \[test\]" &
TAIL_PID=$!

cmd2="${cmd} -testPlatform EditMode -testResults artifacts/test_results_editmode.xml -logFile artifacts/test_log_editmode.txt"
eval $cmd2
EXIT_CODE=$?
kill $TAIL_PID

if [ $EXIT_CODE -ne 0 ]; then
    echo "❌ EditMode tests failed."
    exit $EXIT_CODE
else
    # Double check XML just in case exit code isn't reliable
    if grep -q 'result="Failed"' artifacts/test_results_editmode.xml; then
        echo "❌ EditMode tests failed. Check artifacts/test_results_editmode.xml for details."
        exit 1
    else
        echo "✅ EditMode tests passed."
        exit 0
    fi
fi
