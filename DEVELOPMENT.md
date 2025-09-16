## Running tests
```
./bin/run_tests.sh
```

Or open the unity project and run tests from the "Test Runner" window

## Building and deploying docs

Should only be done from main branch so that live docs are in sync with latest release

```
./bin/build_docs.sh
./bin/deploy_docs.sh # Switches to docs branch and commits+pushes docs/ directory
```