# Usage: ./bin/build_docs.sh

# TODO: Actually open unity project and compile
# (Has to be compiled from the unity-hawk project since that has a special compiler arg configured)
# (Also annoyingly seems like it has to be compiled twice in order for the xml to be produced correctly...?)

# Generate html docs based on Library/ScriptAssemblies/UnityHawk.xml
rm -rf docs/ # clean
cd ./docfx
docfx docfx.json
cd ../