# Usage: ./bin/build_docs.sh

# TODO: Actually open unity project and compile

# Generate html docs based on Library/ScriptAssemblies/UnityHawk.xml
rm -rf docs/ # clean
cd ./docfx
docfx docfx.json
cd ../