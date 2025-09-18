# Usage: ./bin/build_docs.sh

# TODO: Unify this part with run_tests.sh
: "${UNITY_VERSION:=$(grep '^m_EditorVersion:' ProjectSettings/ProjectVersion.txt | awk '{print $2}')}"
: "${UNITY_EXE:=/c/Program Files/Unity/Hub/Editor/${UNITY_VERSION}/Editor/Unity.exe}"

### Compile unity project to generate xml docstrings file
xmlTarget=Library/ScriptAssemblies/UnityHawk.xml

# Clean
rm -f ${xmlTarget};

cmd="\"${UNITY_EXE}\" -projectPath . -batchmode -logFile build_docs.log -executeMethod RecompileForcer.ForceRecompile -quit";
echo $cmd;
eval $cmd;

if [ ! -f "${xmlTarget}" ]; then
    echo "Error: XML docstrings file '${xmlTarget}' was not generated. Exiting without building docs."
    exit 1
fi

### Generate html docs based on generated xml
rm -rf docs/ # clean
cd ./docfx
docfx docfx.json
cd ../