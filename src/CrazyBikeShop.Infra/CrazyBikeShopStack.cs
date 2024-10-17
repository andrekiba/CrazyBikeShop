using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
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
using Pulumi.Command.Local;
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
        const string projectName = "crazybikeshop";
        var stackName = Deployment.Instance.StackName;
        var azureConfig = new Config("azure-native");

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
        
        var excludedDirectories = new[] { "bin", "obj", ".idea", nameof(Infra) };
        var excludedFiles = new[] {".DS_Store", "appsettings.development.json", ".override.yml"};
        
        var buildContext = Path.GetFullPath(Directory.GetParent(Directory.GetCurrentDirectory())!.FullName);
        var buildContextHash = buildContext.GenerateHash(excludedDirectories, excludedFiles);

        const string apiImageName = "api";
        ApiImageTag = Output.Format($"{containerRegistry.LoginServer}/{apiImageName}:latest");

        const string orderProcessorImageName = "op";
        OrderProcessorImageTag = Output.Format($"{containerRegistry.LoginServer}/{orderProcessorImageName}:latest");

        #endregion

        #region ACR commands

        var azAcrBuildAndPush = Output.Format($"az acr build -t $IMAGENAME -r $REGISTRY -f $DOCKERFILE --platform linux/amd64 $CONTEXT");
        
        var apiBuildPushCommand = new Command("api-build-and-push",
            new CommandArgs
            {
                Dir = Directory.GetCurrentDirectory(),
                Create = azAcrBuildAndPush,
                Environment = new InputMap<string>
                {
                    { "IMAGENAME", apiImageName },
                    { "REGISTRY", containerRegistry.Name },
                    { "DOCKERFILE", Path.Combine(buildContext, "CrazyBikeShop.Api", "Dockerfile") },
                    { "CONTEXT", buildContext }
                },
                Triggers = new []
                {
                    buildContextHash
                }
            },
            new CustomResourceOptions
            {
                DeleteBeforeReplace = true
            });
        
        var opBuildPushCommand = new Command("op-build-and-push",
            new CommandArgs
            {
                Dir = Directory.GetCurrentDirectory(),
                Create = azAcrBuildAndPush,
                Environment = new InputMap<string>
                {
                    { "IMAGENAME", orderProcessorImageName},
                    { "REGISTRY", containerRegistry.Name },
                    { "DOCKERFILE", Path.Combine(buildContext, "CrazyBikeShop.OrderProcessor", "Dockerfile") },
                    { "CONTEXT", buildContext }
                },
                Triggers = new []
                {
                    buildContextHash
                }
            },
            new CustomResourceOptions
            {
                DeleteBeforeReplace = true
            });

        #endregion

        #region ACR tasks
        /*
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
                ContextPath = buildContext,
                DockerFilePath = Path.Combine(buildContext, "CrazyBikeShop.Api/Dockerfile"),
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
                ContextPath = buildContext,
                DockerFilePath = Path.Combine(buildContext, "CrazyBikeShop.OrderProcessor/Dockerfile"),
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
        */
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
                    TargetPort = 8080,
                    //ExposedPort = 80
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
                        Image = ApiImageTag,
                        Env =
                        {
                            new EnvironmentVarArgs
                            {
                                Name = "Storage__Tables",
                                Value = storageAccount.PrimaryEndpoints.Apply(e => e.Table)
                            },
                            new EnvironmentVarArgs
                            {
                                Name = "Arm__Subscription",
                                Value = azureConfig.Require("subscriptionId")
                            },
                            new EnvironmentVarArgs
                            {
                                Name = "Arm__ResourceGroup",
                                Value = resourceGroup.Name
                            },
                        }
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
            DependsOn = new InputList<Resource> { apiBuildPushCommand }
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
                        Image = OrderProcessorImageTag,
                        Env =
                        {
                            new EnvironmentVarArgs
                            {
                                Name = "Storage__Tables",
                                Value = storageAccount.PrimaryEndpoints.Apply(e => e.Table)
                            }
                        }
                    }
                }
            }
        }, new CustomResourceOptions
        {
            IgnoreChanges = new List<string> { "tags" },
            DependsOn = new InputList<Resource> { opBuildPushCommand }
        });

        #endregion

        #region Roles
        
        const string storageTableDataContributor = "0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3";
        const string acrPull = "7f951dda-4ed3-4680-a7ca-43fe172d538d";

        var apiTableStorageDataContributorRoleName = $"{projectName}-{stackName}-api-tsdc-role";
        var apiTableStorageDataContributorRole = new RoleAssignment(apiTableStorageDataContributorRoleName, new RoleAssignmentArgs
        {
            PrincipalId = api.Identity.Apply(i => i.PrincipalId),
            PrincipalType = PrincipalType.ServicePrincipal,
            RoleAssignmentName = "c7afc1b0-c65d-43f4-bcab-58f883e78b7f",
            RoleDefinitionId = $"/providers/Microsoft.Authorization/roleDefinitions/{storageTableDataContributor}",
            Scope = storageAccount.Id
        });
        
        var opTableStorageDataContributorRoleName = $"{projectName}-{stackName}-op-tsdc-role";
        var opTableStorageDataContributorRole = new RoleAssignment(opTableStorageDataContributorRoleName, new RoleAssignmentArgs
        {
            PrincipalId = orderProcessor.Identity.Apply(i => i.PrincipalId),
            PrincipalType = PrincipalType.ServicePrincipal,
            RoleAssignmentName = "3af15e95-824a-47b1-b465-dfc5b6a17429",
            RoleDefinitionId = $"/providers/Microsoft.Authorization/roleDefinitions/{storageTableDataContributor}",
            Scope = storageAccount.Id
        });
        
        var apiAcrPullRoleName = $"{projectName}-{stackName}-api-acr-pull-role";
        var apiAcrPullRole = new RoleAssignment(apiAcrPullRoleName, new RoleAssignmentArgs
        {
            PrincipalId = api.Identity.Apply(i => i.PrincipalId),
            PrincipalType = PrincipalType.ServicePrincipal,
            RoleAssignmentName = "a18f913c-84ae-4473-b58b-7d01db4f082e",
            RoleDefinitionId = $"/providers/Microsoft.Authorization/roleDefinitions/{acrPull}",
            Scope = containerRegistry.Id
        });
        
        var opAcrPullRoleName = $"{projectName}-{stackName}-op-acr-pull-role";
        var opAcrPullRole = new RoleAssignment(opAcrPullRoleName, new RoleAssignmentArgs
        {
            PrincipalId = orderProcessor.Identity.Apply(i => i.PrincipalId),
            PrincipalType = PrincipalType.ServicePrincipal,
            RoleAssignmentName = "c1ad4cba-43a3-4ef0-bee5-c247b795d8c6",
            RoleDefinitionId = $"/providers/Microsoft.Authorization/roleDefinitions/{acrPull}",
            Scope = containerRegistry.Id
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