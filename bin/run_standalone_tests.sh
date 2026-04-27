#!/bin/bash
# Usage: ./bin/run_standalone_tests.sh

: "${UNITY_VERSION:=$(grep '^m_EditorVersion:' ProjectSettings/ProjectVersion.txt | awk '{print $2}')}"
: "${UNITY_EXE:=/c/Program Files/Unity/Hub/Editor/${UNITY_VERSION}/Editor/Unity.exe}"

cmd="\"${UNITY_EXE}\"
  -runTests \
  -batchmode \
  $@ \
  -projectPath ."

echo "Running Standalone (win64) tests..."
echo "(No console output for these while running, but errors will be reported if it fails)"

mkdir -p artifacts
> artifacts/test_log_standalonewindows64.txt

cmd3="${cmd} -testPlatform StandaloneWindows64 -testResults artifacts/test_results_standalonewindows64.xml -logFile artifacts/test_log_standalonewindows64.txt"
eval $cmd3
EXIT_CODE=$?

if [ $EXIT_CODE -ne 0 ]; then
    echo "❌ Standalone tests failed to build or run."
    echo "---- Last 50 lines of log (artifacts/test_log_standalonewindows64.txt): ----"
    tail -n 50 artifacts/test_log_standalonewindows64.txt
    echo "----------------------------------------------------------------------------"
    exit $EXIT_CODE
else
    if grep -q 'result="Failed"' artifacts/test_results_standalonewindows64.xml 2>/dev/null; then
        echo "❌ Standalone tests failed. Check artifacts/test_results_standalonewindows64.xml for details."
        # If tests natively failed, let's also dump the failures from XML if possible
        grep -B 2 -A 5 'result="Failed"' artifacts/test_results_standalonewindows64.xml
        exit 1
    else
        echo "✅ Standalone tests passed."
        exit 0
    fi
fi
