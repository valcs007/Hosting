// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Framework.Logging;
using Microsoft.AspNet.Testing;

namespace Microsoft.AspNet.Server.Testing
{
    /// <summary>
    /// Deployer for Kestrel on Unix with CoreCLR.
    /// </summary>
    public class UnixCoreClrDeployer : ApplicationDeployer
    {
        private Process _hostProcess;

        public UnixCoreClrDeployer(DeploymentParameters deploymentParameters, ILogger logger)
            : base(deploymentParameters, logger)
        {
        }

        public override DeploymentResult Deploy()
        {
            // Start timer
            StartTimer();

            var Os = "";
            if (TestPlatformHelper.IsLinux)
            {
                Os = "linux";
            } else if(TestPlatformHelper.IsMac)
            {
                Os = "darwin";
            }

            var runtime = Directory.EnumerateDirectories($"{Environment.GetEnvironmentVariable("HOME")}/.dnx/runtimes/")
                .Where(f => f.Contains($"dnx-coreclr-{Os}")).LastOrDefault();
            var runtimeBin = Path.Combine(runtime, "bin");

            DeploymentParameters.DnxRuntime = new DirectoryInfo(runtimeBin).FullName;

            if (DeploymentParameters.PublishApplicationBeforeDeployment)
            {
                // We use full path to runtime to pack.
                DnuPublish();
            }

            // Launch the host process.
            var hostExitToken = StartCoreCLRHost();

            return new DeploymentResult
            {
                WebRootLocation = DeploymentParameters.ApplicationPath,
                DeploymentParameters = DeploymentParameters,
                ApplicationBaseUri = DeploymentParameters.ApplicationBaseUriHint,
                HostShutdownToken = hostExitToken
            };
        }

        private CancellationToken StartCoreCLRHost()
        {
            if (DeploymentParameters.ServerType != ServerType.Kestrel)
            {
                throw new InvalidOperationException("kestrel is the only valid ServerType for Unix CoreCLR");
            }

            var dnxArgs = $"--appbase \"{DeploymentParameters.ApplicationPath}\" Microsoft.Dnx.ApplicationHost kestrel --server.urls {DeploymentParameters.ApplicationBaseUriHint}";

            Logger.LogInformation("Executing command: dnx {dnxArgs}", dnxArgs);

            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(DeploymentParameters.DnxRuntime, "dnx"),
                Arguments = dnxArgs,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true
            };

            _hostProcess = Process.Start(startInfo);
            _hostProcess.EnableRaisingEvents = true;
            var hostExitTokenSource = new CancellationTokenSource();
            _hostProcess.Exited += (sender, e) =>
            {
                Logger.LogError("Host process {processName} exited with code {exitCode}.", startInfo.FileName, _hostProcess.ExitCode);
                TriggerHostShutdown(hostExitTokenSource);
            };

            Logger.LogInformation("Started {0}. Process Id : {1}", _hostProcess.MainModule.FileName, _hostProcess.Id);

            if (_hostProcess.HasExited)
            {
                Logger.LogError("Host process {processName} exited with code {exitCode} or failed to start.", startInfo.FileName, _hostProcess.ExitCode);
                throw new Exception("Failed to start host");
            }

            return hostExitTokenSource.Token;
        }

        public override void Dispose()
        {
            ShutDownIfAnyHostProcess(_hostProcess);

            if (DeploymentParameters.PublishApplicationBeforeDeployment)
            {
                CleanPublishedOutput();
            }

            InvokeUserApplicationCleanup();

            StopTimer();
        }
    }
}