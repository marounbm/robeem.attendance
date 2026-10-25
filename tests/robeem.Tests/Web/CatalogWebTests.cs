using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using robeem.Contracts.Attendance;
using robeem.Web.ApiClients;
using robeem.Web.Controllers;
using robeem.Web.Models;
using robeem.Web.Navigation;

namespace robeem.Tests.Web;

/// <summary>The vendors, device types and transports screens: the controllers over stand-in clients.</summary>
public sealed class CatalogWebTests
{
    [Fact]
    public async Task TheNewConnectorForm_OffersTheVendorsTheActiveFormatsAndEveryTransport()
    {
        var controller = Connectors(new FakeConnectors());

        var model = Assert.IsType<ConnectorFormViewModel>(Assert.IsType<ViewResult>(await controller.Create(CancellationToken.None)).Model);

        Assert.Equal(["Generic CSV"], model.PayloadFormats.Select(f => f.Name));
        Assert.Equal(["FileUpload", "RestApi"], model.Transports.Select(t => t.Key));
        Assert.Equal(["Acme"], model.Vendors.Select(v => v.DisplayName));
    }

    [Fact]
    public async Task ASavedVendor_GoesBackToTheList_WithAMessage()
    {
        var controller = Vendors(new FakeVendors { Save = ApiSaveResult.Success(3) });

        var result = await controller.Create(new VendorFormViewModel { Input = new VendorInput { DisplayName = "Acme clock" } }, CancellationToken.None);

        Assert.Equal(nameof(VendorsController.Index), Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains("Acme clock", (string)controller.TempData["StatusMessage"]!);
    }

    [Fact]
    public async Task ARefusedVendor_ComesBackOnTheField()
    {
        var vendors = new FakeVendors { Save = ApiSaveResult.Invalid(new Dictionary<string, string> { [nameof(VendorInput.VendorKey)] = "Another vendor already has this key." }) };
        var controller = Vendors(vendors);

        var view = Assert.IsType<ViewResult>(await controller.Create(new VendorFormViewModel { Input = new VendorInput { VendorKey = "dup" } }, CancellationToken.None));

        Assert.Equal("Another vendor already has this key.", controller.ModelState["Input.VendorKey"]!.Errors.Single().ErrorMessage);
        Assert.Equal("dup", Assert.IsType<VendorFormViewModel>(view.Model).Input.VendorKey);
    }

    [Fact]
    public async Task AnUnknownVendor_IsNotFound_WhenEditing()
    {
        var controller = Vendors(new FakeVendors { Save = ApiSaveResult.Missing(), Exists = false });

        Assert.IsType<NotFoundResult>(await controller.Edit(9, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await controller.Edit(9, new VendorFormViewModel(), CancellationToken.None));
    }

    [Fact]
    public async Task ASavedConnector_GoesBackToTheList_WithAMessage()
    {
        var controller = Connectors(new FakeConnectors { Save = ApiSaveResult.Success(3) });

        var result = await controller.Create(new ConnectorFormViewModel { Input = new ConnectorInput { DisplayName = "Acme export" } }, CancellationToken.None);

        Assert.Equal(nameof(ConnectorsController.Index), Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains("Acme export", (string)controller.TempData["StatusMessage"]!);
    }

    [Fact]
    public async Task ARefusedConnector_ComesBackOnTheField_WithTheChoicesToPickFrom()
    {
        var connectors = new FakeConnectors { Save = ApiSaveResult.Invalid(new Dictionary<string, string> { [nameof(ConnectorInput.ConnectorKey)] = "Another connector already has this key." }) };
        var controller = Connectors(connectors);

        var view = Assert.IsType<ViewResult>(await controller.Create(new ConnectorFormViewModel(), CancellationToken.None));

        Assert.Equal("Another connector already has this key.", controller.ModelState["Input.ConnectorKey"]!.Errors.Single().ErrorMessage);
        Assert.NotEmpty(Assert.IsType<ConnectorFormViewModel>(view.Model).Transports);
    }

    [Fact]
    public async Task AnUnknownConnector_IsNotFound_WhenEditing()
    {
        var controller = Connectors(new FakeConnectors { Save = ApiSaveResult.Missing(), Exists = false });

        Assert.IsType<NotFoundResult>(await controller.Edit(9, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await controller.Edit(9, new ConnectorFormViewModel(), CancellationToken.None));
    }

    [Fact]
    public async Task ADeviceType_IsAddedAndSaved_AndARefusalIsShownOnTheList()
    {
        var types = new FakeTypes();
        var controller = WithTempData(new DeviceTypesController(types));

        await controller.Create(new DeviceTypeInput { Name = "Palm reader" }, CancellationToken.None);
        Assert.Contains("Palm reader", (string)controller.TempData["StatusMessage"]!);

        types.Save = ApiSaveResult.Invalid(new Dictionary<string, string> { [nameof(DeviceTypeInput.Name)] = "Another device type already has this name." });
        var refused = await controller.Update(4, new DeviceTypeInput { Name = "Facial" }, CancellationToken.None);

        Assert.Equal(nameof(DeviceTypesController.Index), Assert.IsType<RedirectToActionResult>(refused).ActionName);
        Assert.Contains("already has this name", (string)controller.TempData["ErrorMessage"]!);
        Assert.Equal(["create", "update"], types.Calls);
    }

    [Fact]
    public async Task ATransport_IsRenamed_AndAMissingOneIsNotFound()
    {
        var transports = new FakeTransports();
        var controller = WithTempData(new TransportsController(transports));

        var saved = await controller.Update(2, new TransportInput { DisplayName = "Secure FTP" }, CancellationToken.None);
        transports.Save = ApiSaveResult.Missing();
        var missing = await controller.Update(9, new TransportInput { DisplayName = "x" }, CancellationToken.None);

        Assert.Equal(nameof(TransportsController.Index), Assert.IsType<RedirectToActionResult>(saved).ActionName);
        Assert.Contains("Secure FTP", (string)controller.TempData["StatusMessage"]!);
        Assert.IsType<NotFoundResult>(missing);
    }

    [Theory]
    [InlineData("Vendors")]
    [InlineData("Connectors")]
    [InlineData("DeviceTypes")]
    [InlineData("Transports")]
    public void TheCatalogScreens_AreForIntegrationAdministrators(string controller)
    {
        var item = NavigationMenu.Sections.SelectMany(s => s.Items).Single(i => i.Controller == controller);

        Assert.Equal(["IntegrationAdministrator"], item.Roles);
    }

    private static VendorsController Vendors(FakeVendors vendors) =>
        WithTempData(new VendorsController(vendors));

    private static ConnectorsController Connectors(FakeConnectors connectors) =>
        WithTempData(new ConnectorsController(connectors, new FakeVendors(), new FakeIntegrations(), new FakeTransports()));

    private static T WithTempData<T>(T controller)
        where T : Controller
    {
        controller.TempData = new TempDataDictionary(new DefaultHttpContext(), new NullTempDataProvider());
        return controller;
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }

    private sealed class FakeVendors : IVendorsApiClient
    {
        public ApiSaveResult Save { get; set; } = ApiSaveResult.Success(1);

        public bool Exists { get; set; } = true;

        public Task<IReadOnlyList<VendorListItem>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<VendorListItem>>([new VendorListItem { Id = 1, VendorKey = "acme", DisplayName = "Acme", IsActive = true }]);

        public Task<VendorInput?> GetInputAsync(int id, CancellationToken cancellationToken) => Task.FromResult<VendorInput?>(Exists ? new VendorInput() : null);

        public Task<ApiSaveResult> CreateAsync(VendorInput input, CancellationToken cancellationToken) => Task.FromResult(Save);

        public Task<ApiSaveResult> UpdateAsync(int id, VendorInput input, CancellationToken cancellationToken) => Task.FromResult(Save);
    }

    private sealed class FakeConnectors : IConnectorsApiClient
    {
        public ApiSaveResult Save { get; set; } = ApiSaveResult.Success(1);

        public bool Exists { get; set; } = true;

        public Task<IReadOnlyList<ConnectorListItem>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ConnectorListItem>>([]);

        public Task<ConnectorInput?> GetInputAsync(int id, CancellationToken cancellationToken) => Task.FromResult<ConnectorInput?>(Exists ? new ConnectorInput() : null);

        public Task<ApiSaveResult> CreateAsync(ConnectorInput input, CancellationToken cancellationToken) => Task.FromResult(Save);

        public Task<ApiSaveResult> UpdateAsync(int id, ConnectorInput input, CancellationToken cancellationToken) => Task.FromResult(Save);
    }

    private sealed class FakeTypes : IDeviceTypesApiClient
    {
        public ApiSaveResult Save { get; set; } = ApiSaveResult.Success(1);

        public List<string> Calls { get; } = [];

        public Task<IReadOnlyList<DeviceTypeItem>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<DeviceTypeItem>>([]);

        public Task<ApiSaveResult> CreateAsync(DeviceTypeInput input, CancellationToken cancellationToken)
        {
            Calls.Add("create");
            return Task.FromResult(Save);
        }

        public Task<ApiSaveResult> UpdateAsync(int id, DeviceTypeInput input, CancellationToken cancellationToken)
        {
            Calls.Add("update");
            return Task.FromResult(Save);
        }
    }

    private sealed class FakeTransports : ITransportsApiClient
    {
        public ApiSaveResult Save { get; set; } = ApiSaveResult.Success(1);

        public Task<IReadOnlyList<TransportItem>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<TransportItem>>(
        [
            new TransportItem { Id = 1, Key = "FileUpload", DisplayName = "Manual file upload", IsActive = true },
            new TransportItem { Id = 2, Key = "RestApi", DisplayName = "REST API", IsPull = true, IsActive = true },
        ]);

        public Task<ApiSaveResult> UpdateAsync(int id, TransportInput input, CancellationToken cancellationToken) => Task.FromResult(Save);
    }

    private sealed class FakeIntegrations : IIntegrationsApiClient
    {
        public Task<IReadOnlyList<PayloadFormatOption>> ListPayloadFormatOptionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PayloadFormatOption>>([new PayloadFormatOption { Id = 1, Name = "Generic CSV" }]);

        public Task<IReadOnlyList<IntegrationListItem>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IntegrationListItem?> GetAsync(int id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IntegrationInput?> GetInputAsync(int id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ConnectorOption>> ListConnectorsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<DeviceTypeOption>> ListDeviceTypeOptionsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<EmployeeOption>> ListEmployeeOptionsAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ApiSaveResult> CreateAsync(IntegrationInput input, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ApiSaveResult> UpdateAsync(int id, IntegrationInput input, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IntegrationHealthReport?> GetHealthAsync(int id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<DeviceItem>> ListDevicesAsync(int integrationId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<DeviceItem>> ListAllDevicesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<DeviceItem?> GetDeviceAsync(int integrationId, int deviceId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ApiSaveResult> AddDeviceAsync(int integrationId, DeviceInput input, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ApiSaveResult> UpdateDeviceAsync(int integrationId, int deviceId, DeviceInput input, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ApiSaveResult> DeleteDeviceAsync(int integrationId, int deviceId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<MappingItem>> ListMappingsAsync(int integrationId, int deviceId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ApiSaveResult> AddMappingAsync(int integrationId, int deviceId, MappingInput input, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ApiSaveResult> DeleteMappingAsync(int integrationId, int deviceId, int mappingId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SyncOutcomeResponse> RunAsync(int integrationId, bool reconcile, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RetryFailedResponse?> RetryFailedAsync(int integrationId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<RenormalizeFlaggedResponse?> RenormalizeFlaggedAsync(int integrationId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<RawNormalizedMappingItem>?> ListRawMappingAsync(int integrationId, int? batchId, string? status, int take, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<VendorMappingGuide?> GetRawMappingGuideAsync(int integrationId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
