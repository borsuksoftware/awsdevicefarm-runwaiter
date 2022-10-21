using Amazon.DeviceFarm;
using Amazon.DeviceFarm.Model;
using Amazon.Runtime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BorsukSoftware.Tools.AWSDeviceFarm.RunWaiter
{
    public class Program
    {
        private const string CONST_HELPTEXT = @"AWS Device Farm Test Waiter
===========================

Summary:
This app is designed to make it easy to wait for an AWS Device Farm test job from the command line.

The app will return an exit code of zero in case of success, or non-zero in case of error.

Security Model:
The security variables are read in from environment variables and as such, they should be set accordingly.

Required parameters:
 -project XXX               The name or arn of the AWS device farm project
 -testRun XXX               The name or arn of the test run to wait on

Others:
 -timeout XXX               The number of seconds to wait before giving up (defaults to 30 minutes)

 --help                     Show this help text";


        public static async Task<int> Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine(CONST_HELPTEXT);
                return 0;
            }

            var testRunName = $"Test Run - {DateTime.UtcNow:yyyy-MM-dd HHmmss}";
            string paramProject = null;
            var timeOutSeconds = 30 * 60;

            for (int i = 0; i < args.Length; ++i)
            {
                switch (args[i].ToLower())
                {
                    case "-project":
                        paramProject = args[++i];
                        break;

                    case "-testrun":
                        testRunName = args[++i];
                        break;

                    case "-timeout":
                        {
                            var timeoutStr = args[++i];
                            if (!int.TryParse(timeoutStr, out var s))
                            {
                                Console.WriteLine($"Failed to parse '{s}' as a valid time out");
                                return 1;
                            }

                            if (s < 0)
                            {
                                Console.WriteLine("Negative timeout specified, using 0");
                                s = 0;
                            }

                            timeOutSeconds = s;

                        }
                        break;

                    case "--help":
                        Console.WriteLine(CONST_HELPTEXT);
                        return 0;

                    default:
                        {
                            Console.WriteLine($"Unknown command line arg - {args[i]}");
                            return 1;
                        }
                }
            }

            if (string.IsNullOrEmpty(paramProject))
            {
                Console.WriteLine("No project specified");
                return 1;
            }

            if (string.IsNullOrEmpty(testRunName))
            {
                Console.WriteLine("No test run specified");
                return 1;
            }

            Console.WriteLine("Creating farm client");
            var amazonDeviceFarmClient = new Amazon.DeviceFarm.AmazonDeviceFarmClient(Amazon.RegionEndpoint.USWest2);

            /************************************ Fetch all details from AWS ***********************************/
            var projectArn = paramProject;
            if (!paramProject.StartsWith("arn:"))
            {
                Console.WriteLine("Sourcing project details from AWS");
                var projectList = await amazonDeviceFarmClient.ListProjectsAsync(new ListProjectsRequest());
                var project = projectList.Projects.SingleOrDefault(p => StringComparer.InvariantCultureIgnoreCase.Compare(paramProject, p.Name) == 0);
                if (project == null)
                {
                    Console.WriteLine($"No project name '{paramProject}' found");
                    return 1;
                }

                projectArn = project.Arn;
            }

            var testRunArn = testRunName;
            if (!testRunArn.StartsWith("arn:"))
            {
                Console.WriteLine("Sourcing test run details from AWS");
                var testRuns = await amazonDeviceFarmClient.ListRunsAsync(new ListRunsRequest { Arn = projectArn });
                var testRun = testRuns.Runs.SingleOrDefault(tr => StringComparer.InvariantCultureIgnoreCase.Compare(testRunArn, tr.Name) == 0);
                if (testRun == null)
                {
                    Console.WriteLine($"No test run named '{testRunName}' found");
                    return 1;
                }

                testRunArn = testRun.Arn;
            }

            Console.WriteLine("Settings:");
            Console.WriteLine($" Project: {projectArn}");
            Console.WriteLine($" Test Run: {testRunArn}");
            Console.WriteLine($" Timeout: {timeOutSeconds}s");
            Console.WriteLine();

            Console.WriteLine("Checking status");
            int count = 0;
            ExecutionStatus lastExecutionStatus = null;
            var endTime = DateTime.UtcNow.AddSeconds(timeOutSeconds);
            do
            {
                var testRunObject = await amazonDeviceFarmClient.GetRunAsync(new GetRunRequest { Arn = testRunArn });

                if (testRunObject.Run.Status == ExecutionStatus.COMPLETED)
                {
                    Console.WriteLine();
                    Console.WriteLine(" => completed");
                    return 0;
                }

                if (testRunObject.Run.Status == lastExecutionStatus)
                {
                    Console.Write(".");
                }
                else
                {
                    Console.Write($" => {testRunObject.Run.Status}");
                    lastExecutionStatus = testRunObject.Run.Status;
                }

                ++count;
                await Task.Delay(5000);
            } while (DateTime.UtcNow <= endTime);

            Console.WriteLine("Failed to obtain a status in time - timing out");
            return 1;
        }
    }
}