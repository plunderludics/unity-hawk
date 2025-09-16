// Log tests to console so we can see what's happening when running from cli

using NUnit.Framework.Interfaces;
using UnityEngine.TestRunner;
using UnityEngine;

[assembly:TestRunCallback(typeof(UnityHawk.Tests.TestLogging))]

namespace UnityHawk.Tests {

public class TestLogging : ITestRunCallback
{
    public void RunStarted(ITest testsToRun)
    {
        Debug.Log($"[unity-hawk] [test] Starting test run with {testsToRun.TestCaseCount} cases");
    }

    public void TestStarted(ITest test)
    {
        Debug.Log($"[unity-hawk] [test] Running: {test.FullName}");
    }

    public void TestFinished(ITestResult result)
    {
        Debug.Log($"[unity-hawk] [test] {result.Test.FullName}: {result.ResultState}");
        if (result.ResultState == ResultState.Failure || result.ResultState == ResultState.Error) {
            Debug.Log($"[unity-hawk] [test] {result.Test.FullName}: {result.Message}");
            Debug.Log($"{result.StackTrace}");
        }
    }

    public void RunFinished(ITestResult result)
    {
        Debug.Log($"[unity-hawk] [test] Test run finished: {result.ResultState}");
    }
}
}