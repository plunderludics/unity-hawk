# Usage: ./bin/deploy_docs.sh

# Assumes docs are already built (in docs/ folder)
# Switch to docs branch, pull latest, add and commit docs/ folder, push

# Ensure we're on main branch
CURRENT_BRANCH=$(git branch --show-current)
if [ "$CURRENT_BRANCH" != "main" ]; then
    echo "Error: This script should only be run from the main branch (currently on $CURRENT_BRANCH)"
    exit 1
fi

git checkout docs &&
git pull &&
git add docs/ &&
git commit -m "Update docs" &&
git push

# Return to main branch
git checkout $CURRENT_BRANCH