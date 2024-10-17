using System.Threading.Tasks;
using Pulumi;

namespace CrazyBikeShop.Infra;

internal static class Program
{
    static Task<int> Main() => Deployment.RunAsync<CrazyBikeShopStack>();
}