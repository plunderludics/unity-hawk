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
        Debug.Log($"Starting test run with {testsToRun.TestCaseCount} cases");
    }

    public void TestStarted(ITest test)
    {
        Debug.Log($"Running: {test.FullName}");
    }

    public void TestFinished(ITestResult result)
    {
        Debug.Log($"{result.Test.FullName}: {result.ResultState}");
    }

    public void RunFinished(ITestResult result)
    {
        Debug.Log($"Test run finished: {result.ResultState}");
    }
}
}