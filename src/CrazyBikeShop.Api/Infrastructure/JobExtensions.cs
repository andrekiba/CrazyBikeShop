using System.Linq;
using Azure.ResourceManager.AppContainers.Models;

namespace CrazyBikeShop.Api.Infrastructure;

public static class JobExtensions
{
    public static ContainerAppJobExecutionTemplate ToExecutionTemplate(this ContainerAppJobTemplate template)
    {
        var executionTemplate = new ContainerAppJobExecutionTemplate();
        template.Containers.ToList().ForEach(c =>
        {
            var executionContainer = new JobExecutionContainer
            {
                Name = c.Name,
                Image = c.Image,
                Resources = c.Resources,
            };
            c.Command.ToList().ForEach(cmd => executionContainer.Command.Add(cmd));
            c.Args.ToList().ForEach(a => executionContainer.Args.Add(a));
            c.Env.ToList().ForEach(e => executionContainer.Env.Add(e));
            executionTemplate.Containers.Add(executionContainer);
        });
        template.InitContainers.ToList().ForEach(c =>
        {
            var executionInitContainer = new JobExecutionContainer
            {
                Name = c.Name,
                Image = c.Image,
                Resources = c.Resources,
            };
            c.Command.ToList().ForEach(cmd => executionInitContainer.Command.Add(cmd));
            c.Args.ToList().ForEach(a => executionInitContainer.Args.Add(a));
            c.Env.ToList().ForEach(e => executionInitContainer.Env.Add(e));
            executionTemplate.InitContainers.Add(executionInitContainer);
        });
        
        return executionTemplate;
    }
}