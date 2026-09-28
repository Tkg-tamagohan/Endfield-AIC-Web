using System.Reflection;

namespace EndfieldAicWeb.Domain.Tests;

public class ScaffoldTests
{
    [Fact(DisplayName = "SCAF-01: Domain アセンブリを読み込める")]
    public void DomainAssemblyLoads()
    {
        var assembly = Assembly.Load("EndfieldAicWeb.Domain");
        Assert.NotNull(assembly);
        Assert.Equal("EndfieldAicWeb.Domain", assembly.GetName().Name);
    }
}
