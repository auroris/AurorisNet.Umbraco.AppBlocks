using AppBlocks.InteractiveReports.Reports;
using Microsoft.Extensions.Configuration;

namespace AppBlocks.InteractiveReports.Tests;

public sealed class ApiRootRouteTests
{
    [Fact]
    public void Interactive_report_routes_follow_AppBlocks_ApiRoot_and_default_to_api()
    {
        Assert.Equal("/api/reports", InteractiveReportsSite.ReportsRouteFor(null));
        Assert.Equal("/api/download", InteractiveReportsSite.DownloadRouteFor(null));
        Assert.Equal("/api/reports/__saved-views-setup", SavedViewsSetupNote.RouteFor(null));

        var custom = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [AppBlocksRoutes.Setting] = "/services" }).Build();
        Assert.Equal("/services/reports", InteractiveReportsSite.ReportsRouteFor(custom));
        Assert.Equal("/services/download", InteractiveReportsSite.DownloadRouteFor(custom));
        Assert.Equal("/services/reports/__saved-views-setup", SavedViewsSetupNote.RouteFor(custom));
    }
}
