using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Pulumi;
using Pulumi.AzureNative.Resources;
using Deployment = Pulumi.Deployment;
using Pulumi.AzureNative.App.Inputs;
using Pulumi.AzureNative.Authorization;
using Pulumi.AzureNative.ContainerRegistry;
using Pulumi.AzureNative.ContainerRegistry.Inputs;
using Pulumi.AzureNative.OperationalInsights;
using Pulumi.AzureNative.OperationalInsights.Inputs;
using ASB = Pulumi.AzureNative.ServiceBus;
using App = Pulumi.AzureNative.App;
using ACR = Pulumi.AzureNative.ContainerRegistry;
using Resource = Pulumi.Resource;
using Storage = Pulumi.AzureNative.Storage;

namespace CrazyBikeShop.Infra;

public class CrazyBikeShopStack : Stack
{
    #region Outputs
    [Output] public Output<string> ApiImageTag { get; set; }
    [Output] public Output<string> OrderProcessorImageTag { get; set; }
    [Output] public Output<string> ApiUrl { get; set; }

    #endregion

    public CrazyBikeShopStack()
    {
        
#if DEBUG
        /*
        while (!Debugger.IsAttached)
        {
            Thread.Sleep(100);
        }
        */
#endif

        const string projectName = "crazybikeshop";
        var stackName = Deployment.Instance.StackName;

        #region Resource Group

        var resourceGroupName = $"{projectName}-{stackName}-rg";
        var resourceGroup = new ResourceGroup(resourceGroupName, new ResourceGroupArgs
        {
            ResourceGroupName = resourceGroupName
        });

        #endregion

        #region Azure Storage

        var storageAccountName = $"{projectName}{stackName}st";
        var storageAccount = new Storage.StorageAccount(storageAccountName, new Storage.StorageAccountArgs
        {
            AccountName = storageAccountName,
            ResourceGroupName = resourceGroup.Name,
            Sku = new Storage.Inputs.SkuArgs
            {
                Name = Storage.SkuName.Standard_LRS
            },
            Kind = Storage.Kind.StorageV2
        });

        var ordersTable  = new Storage.Table("orders", new Storage.TableArgs
        {
            TableName = "orders",
            AccountName = storageAccount.Name,
            ResourceGroupName = resourceGroup.Name
        });

        #endregion

        #region Log Analytics

        var logWorkspaceName = $"{projectName}-{stackName}-log";
        var logWorkspace = new Workspace(logWorkspaceName, new WorkspaceArgs
        {
            WorkspaceName = logWorkspaceName,
            ResourceGroupName = resourceGroup.Name,
            Sku = new WorkspaceSkuArgs { Name = "PerGB2018" },
            RetentionInDays = 30
        });

        var logWorkspaceSharedKeys = Output.Tuple(resourceGroup.Name, logWorkspace.Name).Apply(items =>
            GetSharedKeys.InvokeAsync(new GetSharedKeysArgs
            {
                ResourceGroupName = items.Item1,
                WorkspaceName = items.Item2
            }));

        #endregion

        #region Container Registry

        var containerRegistryName = $"{projectName}{stackName}cr";
        var containerRegistry = new Registry(containerRegistryName, new RegistryArgs
        {
            RegistryName = containerRegistryName,
            ResourceGroupName = resourceGroup.Name,
            Sku = new SkuArgs { Name = "Basic" },
            AdminUserEnabled = true
        });

        var credentials = Output.Tuple(resourceGroup.Name, containerRegistry.Name).Apply(items =>
            ListRegistryCredentials.InvokeAsync(new ListRegistryCredentialsArgs
            {
                ResourceGroupName = items.Item1,
                RegistryName = items.Item2
            }));
        var adminUsername = credentials.Apply(c => c.Username);
        var adminPassword = credentials.Apply(c => c.Passwords[0].Value);

        #endregion

        #region Docker build context and image tags

        const string apiImageName = $"{projectName}-api";
        ApiImageTag = Output.Format($"{containerRegistry.LoginServer}/{apiImageName}:latest");

        const string orderProcessorImageName = $"{projectName}-op";
        OrderProcessorImageTag = Output.Format($"{containerRegistry.LoginServer}/{orderProcessorImageName}:latest");

        #endregion

        #region ACR tasks

        const string apiBuildTaskName = "api-build-task";
        var apiBuildTask = new ACR.Task(apiBuildTaskName, new TaskArgs
        {
            TaskName = apiBuildTaskName,
            RegistryName = containerRegistry.Name,
            ResourceGroupName = resourceGroup.Name,
            Status = ACR.TaskStatus.Enabled,
            IsSystemTask = false,
            AgentConfiguration = new AgentPropertiesArgs
            {
                Cpu = 2
            },
            Identity = new IdentityPropertiesArgs
            {
                Type = ACR.ResourceIdentityType.SystemAssigned
            },
            Platform = new PlatformPropertiesArgs
            {
                Architecture = Architecture.Amd64,
                Os = OS.Linux
            },
            Step = new DockerBuildStepArgs
            {
                ContextPath = "./../",
                DockerFilePath = "./../CrazyBikeShop.Api/Dockerfile",
                ImageNames =
                {
                    ApiImageTag
                },
                IsPushEnabled = true,
                NoCache = false,
                Type = "Docker"
            }
        });

        const string apiBuildTaskRunName = "api-build-task-run";
        var apiBuildTaskRun = new TaskRun(apiBuildTaskRunName, new TaskRunArgs
        {
            RegistryName = containerRegistry.Name,
            ResourceGroupName = resourceGroup.Name,
            ForceUpdateTag = ApiImageTag,
            RunRequest = new TaskRunRequestArgs
            {
                TaskId = apiBuildTask.Id,
                Type = "TaskRunRequest"
            }
        });

        const string orderProcessorBuildTaskName = "op-build-task";
        var orderProcessorBuildTask = new ACR.Task(orderProcessorBuildTaskName, new TaskArgs
        {
            TaskName = orderProcessorBuildTaskName,
            RegistryName = containerRegistry.Name,
            ResourceGroupName = resourceGroup.Name,
            Status = ACR.TaskStatus.Enabled,
            IsSystemTask = false,
            AgentConfiguration = new AgentPropertiesArgs
            {
                Cpu = 2
            },
            Identity = new IdentityPropertiesArgs
            {
                Type = ACR.ResourceIdentityType.SystemAssigned
            },
            Platform = new PlatformPropertiesArgs
            {
                Architecture = Architecture.Amd64,
                Os = OS.Linux
            },
            Step = new DockerBuildStepArgs
            {
                ContextPath = "./../",
                DockerFilePath = "./../CrazyBikeShop.OrderProcessor/Dockerfile",
                ImageNames =
                {
                    OrderProcessorImageTag
                },
                IsPushEnabled = true,
                NoCache = false,
                Type = "Docker"
            }
        });

        const string orderProcessorBuildTaskRunName = "op-build-task-run";
        var orderProcessorBuildTaskRun = new TaskRun(orderProcessorBuildTaskRunName, new TaskRunArgs
        {
            RegistryName = containerRegistry.Name,
            ResourceGroupName = resourceGroup.Name,
            ForceUpdateTag = ApiImageTag,
            RunRequest = new TaskRunRequestArgs
            {
                TaskId = orderProcessorBuildTask.Id,
                Type = "TaskRunRequest"
            }
        });

        #endregion

        #region Container Apps

        var kubeEnvName = $"{projectName}-{stackName}-env";
        var kubeEnv = new App.ManagedEnvironment(kubeEnvName, new App.ManagedEnvironmentArgs
        {
            EnvironmentName = kubeEnvName,
            ResourceGroupName = resourceGroup.Name,
            AppLogsConfiguration = new AppLogsConfigurationArgs
            {
                Destination = "log-analytics",
                LogAnalyticsConfiguration = new LogAnalyticsConfigurationArgs
                {
                    CustomerId = logWorkspace.CustomerId,
                    SharedKey = logWorkspaceSharedKeys.Apply(r => r.PrimarySharedKey)
                }
            }
        });

        var apiName = $"{projectName}-{stackName}-ca-api";
        var api = new App.ContainerApp(apiName, new App.ContainerAppArgs
        {
            ContainerAppName = apiName,
            ResourceGroupName = resourceGroup.Name,
            EnvironmentId = kubeEnv.Id,
            Identity = new ManagedServiceIdentityArgs
            {
                Type = App.ManagedServiceIdentityType.SystemAssigned
            },  
            Configuration = new ConfigurationArgs
            {
                ActiveRevisionsMode = App.ActiveRevisionsMode.Single,
                Ingress = new IngressArgs
                {
                    External = true,
                    TargetPort = 80
                },
                Registries =
                {
                    new RegistryCredentialsArgs
                    {
                        Server = containerRegistry.LoginServer,
                        Username = adminUsername,
                        PasswordSecretRef = $"{containerRegistryName}-admin-pwd"
                    }
                },
                Secrets =
                {
                    new SecretArgs
                    {
                        Name = $"{containerRegistryName}-admin-pwd",
                        Value = adminPassword
                    }
                }
            },
            Template = new TemplateArgs
            {
                Containers =
                {
                    new ContainerArgs
                    {
                        Name = apiImageName,
                        Image = ApiImageTag
                    }
                },
                Scale = new ScaleArgs
                {
                    MinReplicas = 1,
                    MaxReplicas = 1
                    /*Rules = new List<ScaleRuleArgs>
                    {
                        new ScaleRuleArgs
                        {
                            Name = "api-http-requests",
                            Http = new HttpScaleRuleArgs
                            {
                                Metadata =
                                {
                                    { "concurrentRequests", "50" }
                                }
                            }
                        }
                    }*/
                }
            }
        }, new CustomResourceOptions
        {
            IgnoreChanges = new List<string> { "tags" },
            DependsOn = new InputList<Resource> { apiBuildTaskRun }
        });
        ApiUrl = Output.Format($"https://{api.Configuration.Apply(c => c.Ingress).Apply(i => i.Fqdn)}/swagger");

        var orderProcessorName = $"{projectName}-{stackName}-job-op";
        var orderProcessor = new App.Job(orderProcessorName, new App.JobArgs
        {
            JobName = orderProcessorName,
            ResourceGroupName = resourceGroup.Name,
            EnvironmentId = kubeEnv.Id,
            Identity = new ManagedServiceIdentityArgs
            {
                Type = App.ManagedServiceIdentityType.SystemAssigned
            },
            Configuration = new JobConfigurationArgs
            {
                TriggerType = App.TriggerType.Manual,
                EventTriggerConfig = new JobConfigurationEventTriggerConfigArgs
                {
                    Parallelism = 1,
                    ReplicaCompletionCount = 1,
                    Scale = new JobScaleArgs
                    {
                        MinExecutions = 1,
                        MaxExecutions = 1
                    }
                },
                ReplicaTimeout = 120,
                ReplicaRetryLimit = 2,
                Registries =
                {
                    new RegistryCredentialsArgs
                    {
                        Server = containerRegistry.LoginServer,
                        Username = adminUsername,
                        PasswordSecretRef = $"{containerRegistryName}-admin-pwd"
                    }
                },
                Secrets =
                {
                    new SecretArgs
                    {
                        Name = $"{containerRegistryName}-admin-pwd",
                        Value = adminPassword
                    }
                }
            },
            Template = new JobTemplateArgs
            {
                Containers =
                {
                    new ContainerArgs
                    {
                        Name = orderProcessorImageName,
                        Image = OrderProcessorImageTag
                    }
                }
            }
        }, new CustomResourceOptions
        {
            IgnoreChanges = new List<string> { "tags" },
            DependsOn = new InputList<Resource> { orderProcessorBuildTaskRun }
        });

        #endregion

        #region Roles

        var apiTableStorageDataContributorRole = new RoleAssignment("apiTableStorageDataContributorRole", new RoleAssignmentArgs
        {
            PrincipalId = api.Identity.Apply(i => i.PrincipalId),
            RoleDefinitionId = "Storage Table Data Contributor",
            Scope = storageAccount.Id
        });
        
        var opTableStorageDataContributorRole = new RoleAssignment("opTableStorageDataContributorRole", new RoleAssignmentArgs
        {
            PrincipalId = orderProcessor.Identity.Apply(i => i.PrincipalId),
            RoleDefinitionId = "Storage Table Data Contributor",
            Scope = storageAccount.Id
        });

        #endregion
    }

    #region Methods

    static async Task<string> GetAsbPrimaryConnectionString(string resourceGroupName, string namespaceName)
    {
        var result = await ASB.ListNamespaceKeys.InvokeAsync(new ASB.ListNamespaceKeysArgs
        {
            AuthorizationRuleName = "RootManageSharedAccessKey",
            NamespaceName = namespaceName,
            ResourceGroupName = resourceGroupName
        });
        return result.PrimaryConnectionString;
    }

    #endregion
}