# Usage: ./bin/run_tests.sh

: "${UNITY_VERSION:=$(grep '^m_EditorVersion:' ProjectSettings/ProjectVersion.txt | awk '{print $2}')}"
: "${UNITY_EXE:=/c/Program Files/Unity/Hub/Editor/${UNITY_VERSION}/Editor/Unity.exe}"

# Seems like we can't redirect logs to stdout so just write to file and tail -f
> test_log.txt

cmd="\"${UNITY_EXE}\"
  -runTests \
  -batchmode \
  -projectPath . \
  -testResults test_results.xml \
  -logFile test_log.txt"

tail -f test_log.txt | grep "\[unity-hawk\] \[test\]" &

echo "Running EditMode tests"
cmd2="${cmd} -testPlatform EditMode -testResults test_results_editmode.xml"
eval $cmd2

echo "Running PlayMode tests"
cmd2="${cmd} -testPlatform PlayMode -testResults test_results_playmode.xml"
eval $cmd2

echo "Running standalone (win64) tests"
cmd3="${cmd} -testPlatform StandaloneWindows64 -testResults test_results_standalonewindows64.xml"
eval $cmd3

echo "Tests complete"
