# Usage: ./bin/run_tests.sh

# Heads up - the TestRamXXX tests seem to be flaky when run from this script, sometimes fail, but they seem fine when run from editor, not sure why 

: "${UNITY_VERSION:=$(grep '^m_EditorVersion:' ProjectSettings/ProjectVersion.txt | awk '{print $2}')}"
: "${UNITY_EXE:=/c/Program Files/Unity/Hub/Editor/${UNITY_VERSION}/Editor/Unity.exe}"

cmd="\"${UNITY_EXE}\"
  -runTests \
  -batchmode \
  -projectPath ."

echo "Running EditMode tests"

# (Seems like we can't redirect logs to stdout so just write to file and tail -f)
> test_log_editmode.txt
tail -f test_log_editmode.txt | grep "\[unity-hawk\] \[test\]" &
TAIL_PID=$!

cmd2="${cmd} -testPlatform EditMode -testResults test_results_editmode.xml -logFile test_log_editmode.txt"
eval $cmd2
kill $TAIL_PID

echo "Running PlayMode tests"

> test_log_playmode.txt
tail -f test_log_playmode.txt | grep "\[unity-hawk\] \[test\]" &
TAIL_PID=$!

cmd2="${cmd} -testPlatform PlayMode -testResults test_results_playmode.xml -logFile test_log_playmode.txt"
eval $cmd2
kill $TAIL_PID

# For some reason nothing gets logged from standalone player when running from cli - fine, can just check the xml or run from editor
echo "Running standalone (win64) tests (no console output for these)"
cmd3="${cmd} -testPlatform StandaloneWindows64 -testResults test_results_standalonewindows64.xml -logFile test_log_standalonewindows64.txt"
eval $cmd3

echo "Tests complete"
