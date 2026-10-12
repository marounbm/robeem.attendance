using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using robeem.Contracts.Attendance;
using robeem.Web.ApiClients;
using robeem.Web.Controllers;
using robeem.Web.Models;
using robeem.Web.Navigation;

namespace robeem.Tests.Web;

/// <summary>The file formats screens: the controller over a stand-in client.</summary>
public sealed class FileFormatsWebTests
{
    [Fact]
    public void TheNewForm_OffersSpareRowsForLabels()
    {
        var controller = WithTempData(new FileFormatsController(new FakeFormats()));

        var model = Assert.IsType<FileFormatFormViewModel>(Assert.IsType<ViewResult>(controller.Create()).Model);

        Assert.Equal(4, model.Input.EventCodes.Count);
        Assert.All(model.Input.EventCodes, c => Assert.True(string.IsNullOrEmpty(c.Code)));
    }

    [Fact]
    public async Task ASavedFormat_GoesBackToTheList_WithAMessage()
    {
        var formats = new FakeFormats { Save = ApiSaveResult.Success(3) };
        var controller = WithTempData(new FileFormatsController(formats));

        var result = await controller.Create(new FileFormatFormViewModel { Input = new FileFormatInput { Name = "Branch export" } }, CancellationToken.None);

        Assert.Equal(nameof(FileFormatsController.Index), Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Contains("Branch export", (string)controller.TempData["StatusMessage"]!);
    }

    [Fact]
    public async Task ARefusal_ComesBackOnTheField_AndKeepsTheLabelsTyped()
    {
        var formats = new FakeFormats { Save = ApiSaveResult.Invalid(new Dictionary<string, string> { [nameof(FileFormatInput.TimestampFormats)] = "Give at least one pattern." }) };
        var controller = WithTempData(new FileFormatsController(formats));
        var model = new FileFormatFormViewModel { Input = new FileFormatInput { EventCodes = [new FileFormatEventCodeInput { Code = "I", EventType = "CheckIn" }] } };

        var view = Assert.IsType<ViewResult>(await controller.Create(model, CancellationToken.None));

        Assert.Equal("Give at least one pattern.", controller.ModelState["Input.TimestampFormats"]!.Errors.Single().ErrorMessage);
        var shown = Assert.IsType<FileFormatFormViewModel>(view.Model);
        Assert.Equal(["I"], shown.Input.EventCodes.Where(c => !string.IsNullOrEmpty(c.Code)).Select(c => c.Code));
        Assert.Equal(5, shown.Input.EventCodes.Count);
    }

    [Fact]
    public async Task AnUnknownFormat_IsNotFound_WhenEditing()
    {
        var controller = WithTempData(new FileFormatsController(new FakeFormats { Save = ApiSaveResult.Missing() }));

        Assert.IsType<NotFoundResult>(await controller.Edit(9, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await controller.Edit(9, new FileFormatFormViewModel(), CancellationToken.None));
    }

    [Fact]
    public async Task ATrial_NeedsAFile_AndShowsTheRowsReadFromIt()
    {
        var formats = new FakeFormats
        {
            Preview = new FileFormatPreview { TotalRows = 1, Rows = [new FileFormatPreviewRow { RowNumber = 1, ExternalEmployeeId = "E-1" }] },
        };
        var controller = WithTempData(new FileFormatsController(formats));

        var withoutFile = Assert.IsType<ViewResult>(await controller.Preview(null, new FileFormatFormViewModel(), null, CancellationToken.None));
        Assert.Equal(nameof(FileFormatsController.Create), withoutFile.ViewName);
        Assert.Equal(0, formats.Previews);
        Assert.Contains("Choose a sample file", controller.ModelState[string.Empty]!.Errors.Single().ErrorMessage);

        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("E-1;04/10/2026;09:00"));
        var file = new FormFile(content, 0, content.Length, "sample", "sample.csv");
        var withFile = Assert.IsType<ViewResult>(await controller.Preview(4, new FileFormatFormViewModel(), file, CancellationToken.None));

        Assert.Equal(nameof(FileFormatsController.Edit), withFile.ViewName);
        Assert.Equal(("sample.csv", 1), (formats.PreviewedFile, formats.Previews));
        Assert.Equal("E-1", Assert.IsType<FileFormatFormViewModel>(withFile.Model).Preview!.Rows.Single().ExternalEmployeeId);
    }

    [Fact]
    public void TheFileFormatsScreen_IsForIntegrationAdministrators()
    {
        var item = NavigationMenu.Sections.SelectMany(s => s.Items).Single(i => i.Controller == "FileFormats");

        Assert.Contains("IntegrationAdministrator", item.Roles);
    }

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

    private sealed class FakeFormats : IFileFormatsApiClient
    {
        public ApiSaveResult Save { get; set; } = ApiSaveResult.Success(1);

        public FileFormatPreview Preview { get; set; } = new();

        public int Previews { get; private set; }

        public string? PreviewedFile { get; private set; }

        public Task<IReadOnlyList<FileFormatListItem>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<FileFormatListItem>>([]);

        public Task<FileFormatInput?> GetInputAsync(int id, CancellationToken cancellationToken) => Task.FromResult<FileFormatInput?>(null);

        public Task<ApiSaveResult> CreateAsync(FileFormatInput input, CancellationToken cancellationToken) => Task.FromResult(Save);

        public Task<ApiSaveResult> UpdateAsync(int id, FileFormatInput input, CancellationToken cancellationToken) => Task.FromResult(Save);

        public Task<ApiCommandResult<FileFormatPreview>> PreviewAsync(FileFormatInput input, Stream file, string fileName, CancellationToken cancellationToken)
        {
            Previews++;
            PreviewedFile = fileName;
            return Task.FromResult(ApiCommandResult<FileFormatPreview>.Ok(Preview));
        }
    }
}
