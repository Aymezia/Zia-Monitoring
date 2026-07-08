using ZiaMonitoring_App.Application;
using Xunit;

namespace ZiaMonitoring.Tests;

public sealed class SystemHealthServiceTests
{
    private const string QcOutputWithDependency = """
        [SC] QueryServiceConfig SUCCESS

        SERVICE_NAME: wuauserv
                TYPE               : 20  WIN32_SHARE_PROCESS
                START_TYPE         : 3   DEMAND_START
                ERROR_CONTROL      : 1   NORMAL
                BINARY_PATH_NAME   : C:\Windows\system32\svchost.exe -k netsvcs -p
                LOAD_ORDER_GROUP   :
                TAG                : 0
                DISPLAY_NAME       : Windows Update
                DEPENDENCIES       : RPCSS
                SERVICE_START_NAME : LocalSystem
        """;

    private const string QcOutputNoDependency = """
        [SC] QueryServiceConfig SUCCESS

        SERVICE_NAME: Spooler
                TYPE               : 110  WIN32_OWN_PROCESS (interactive)
                START_TYPE         : 2   AUTO_START
                ERROR_CONTROL      : 1   NORMAL
                BINARY_PATH_NAME   : C:\Windows\System32\spoolsv.exe
                LOAD_ORDER_GROUP   : SpoolerGroup
                TAG                : 0
                DISPLAY_NAME       : Print Spooler
                DEPENDENCIES       : (none)
                SERVICE_START_NAME : LocalSystem
        """;

    private const string DependOutputWithResults = """
        [SC] EnumDependentServices: EnumServicesStatusEx SUCCESS

        SERVICE_NAME: WinHttpAutoProxySvc
        DISPLAY_NAME: WinHTTP Web Proxy Auto-Discovery Service

        SERVICE_NAME: BITS
        DISPLAY_NAME: Background Intelligent Transfer Service
        """;

    private const string DependOutputEmpty = """
        [SC] EnumDependentServices: EnumServicesStatusEx SUCCESS

        """;

    [Fact]
    public void ParseServiceDependencyInfo_ExtraitLeStartTypeEtLesDependances()
    {
        var info = SystemHealthService.ParseServiceDependencyInfo("wuauserv", QcOutputWithDependency, DependOutputWithResults);

        Assert.Equal("3   DEMAND_START", info.StartType);
        Assert.Contains("RPCSS", info.DependsOn);
        Assert.Equal(2, info.RequiredBy.Count);
        Assert.Contains("WinHttpAutoProxySvc", info.RequiredBy);
        Assert.Contains("BITS", info.RequiredBy);
    }

    [Fact]
    public void ParseServiceDependencyInfo_AucuneDependance_ListesVides()
    {
        var info = SystemHealthService.ParseServiceDependencyInfo("Spooler", QcOutputNoDependency, DependOutputEmpty);

        Assert.Empty(info.DependsOn);
        Assert.Empty(info.RequiredBy);
        Assert.Equal("2   AUTO_START", info.StartType);
    }
}
