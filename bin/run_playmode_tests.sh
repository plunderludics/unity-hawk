#!/bin/bash
# Usage: ./bin/run_playmode_tests.sh

: "${UNITY_VERSION:=$(grep '^m_EditorVersion:' ProjectSettings/ProjectVersion.txt | awk '{print $2}')}"
: "${UNITY_EXE:=/c/Program Files/Unity/Hub/Editor/${UNITY_VERSION}/Editor/Unity.exe}"

cmd="\"${UNITY_EXE}\"
  -runTests \
  -batchmode \
  -projectPath ."

echo "Running PlayMode tests..."

mkdir -p artifacts
> artifacts/test_log_playmode.txt
tail -f artifacts/test_log_playmode.txt | grep "\[unity-hawk\] \[test\]" &
TAIL_PID=$!

cmd2="${cmd} -testPlatform PlayMode -testResults artifacts/test_results_playmode.xml -logFile artifacts/test_log_playmode.txt"
eval $cmd2
EXIT_CODE=$?
kill $TAIL_PID

if [ $EXIT_CODE -ne 0 ]; then
    echo "❌ PlayMode tests failed."
    exit $EXIT_CODE
else
    if grep -q 'result="Failed"' artifacts/test_results_playmode.xml 2>/dev/null; then
        echo "❌ PlayMode tests failed. Check artifacts/test_results_playmode.xml for details."
        exit 1
    else
        echo "✅ PlayMode tests passed."
        exit 0
    fi
fi
