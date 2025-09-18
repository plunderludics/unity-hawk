# Usage: ./bin/deploy_docs.sh

# Assumes docs are already built (in docs/ folder)
# Switch to docs branch, pull latest, add and commit docs/ folder, push

# Ensure we're on main branch
CURRENT_BRANCH=$(git branch --show-current)
if [ "$CURRENT_BRANCH" != "main" ]; then
    echo "Error: This script should only be run from the main branch (currently on $CURRENT_BRANCH)"
    exit 1
fi

if [ ! -d "docs" ]; then
    echo "Error: docs folder does not exist. Please build the documentation (./bin/build_docs.sh) first."
    exit 1
fi


# Backup current docs folder from main
rm -rf /tmp/unityhawk_docs/
cp -r docs/ /tmp/unityhawk_docs

git checkout docs &&
git pull &&
# Restore the latest docs from main
# (Clean existing docs to ensure old files get removed)
rm -rf docs/ &&
mkdir docs/ &&
cp -r /tmp/unityhawk_docs/* docs/ &&
git add -A docs/ &&
git commit -m "Update docs" &&
git push

# Clean up backup
rm -rf /tmp/unityhawk_docs/

# Return to main branch
git checkout $CURRENT_BRANCH