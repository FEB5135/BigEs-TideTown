using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace IdleTown.Tests
{
    public static class WallfixTestRunner
    {
        static TestRunnerApi runner;

        [MenuItem("Tools/Idle Town/Wallfix/Run Integration Tests")]
        public static void RunIntegration()
        {
            runner = ScriptableObject.CreateInstance<TestRunnerApi>();
            runner.RegisterCallbacks(new Results());
            runner.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.EditMode,
                assemblyNames = new[] { "IdleTown.EditModeTests" }
            }));
        }

        [MenuItem("Tools/Idle Town/Wallfix/Run Wall Tests")]
        public static void Run()
        {
            runner = ScriptableObject.CreateInstance<TestRunnerApi>();
            runner.RegisterCallbacks(new Results());
            runner.Execute(new ExecutionSettings(new Filter
            {
                testMode = TestMode.EditMode,
                testNames = new[] { "IdleTown.Tests.WallConstructionModelTests", "IdleTown.Tests.WallConstructionControllerTests",
                    "IdleTown.Tests.WallfixRegressionTests", "IdleTown.Tests.WellWallIntegrationTests" }
            }));
        }

        sealed class Results : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                Directory.CreateDirectory("Logs");
                TestRunnerApi.SaveResultToFile(result, Path.GetFullPath("Logs/Wallfix-tests.xml"));
                Debug.Log($"Wallfix tests: {result.PassCount} passed, {result.FailCount} failed, {result.SkipCount} skipped.");
            }
        }
    }
}
