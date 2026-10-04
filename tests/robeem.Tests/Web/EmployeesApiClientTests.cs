using System.Net;
using robeem.Contracts.HR.Employees;
using robeem.Web.ApiClients;

namespace robeem.Tests.Web;

public sealed class EmployeesApiClientTests
{
    [Fact]
    public async Task List_SendsSearchAndInactiveFilter()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, """[{"id":7,"employeeNumber":"E-7","fullName":"Ada Lovelace","isActive":true}]""");
        var client = new EmployeesApiClient(handler.CreateClient());

        var employees = await client.ListAsync("ada lo", includeInactive: true, CancellationToken.None);

        Assert.Equal("/api/employees?includeInactive=true&search=ada%20lo", handler.Requests.Single().RequestUri!.PathAndQuery);
        var employee = Assert.Single(employees);
        Assert.Equal(7, employee.Id);
        Assert.Equal("Ada Lovelace", employee.FullName);
    }

    [Fact]
    public async Task Get_ReturnsNullWhenNotFound()
    {
        var client = new EmployeesApiClient(new StubHttpHandler(HttpStatusCode.NotFound).CreateClient());

        Assert.Null(await client.GetAsync(42, CancellationToken.None));
    }

    [Fact]
    public async Task Create_ReturnsTheNewId()
    {
        var client = new EmployeesApiClient(new StubHttpHandler(HttpStatusCode.Created, """{"id":12}""").CreateClient());

        var result = await client.CreateAsync(new EmployeeInput(), CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(12, result.EmployeeId);
    }

    [Fact]
    public async Task Create_MapsValidationProblemToInputFields()
    {
        const string problem = """
            {
              "title": "One or more validation errors occurred.",
              "status": 400,
              "errors": {
                "EmployeeNumber": ["Another employee already has this employee number."],
                "$.hireDate": ["The JSON value could not be converted."],
                "input": ["The input field is required."]
              }
            }
            """;
        var client = new EmployeesApiClient(new StubHttpHandler(HttpStatusCode.BadRequest, problem, "application/problem+json").CreateClient());

        var result = await client.CreateAsync(new EmployeeInput(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal("Another employee already has this employee number.", result.Errors[nameof(EmployeeInput.EmployeeNumber)]);
        Assert.Equal("The JSON value could not be converted.", result.Errors[nameof(EmployeeInput.HireDate)]);
        Assert.Equal("The input field is required.", result.Errors[string.Empty]);
    }

    [Fact]
    public async Task Update_ReportsMissingEmployee()
    {
        var client = new EmployeesApiClient(new StubHttpHandler(HttpStatusCode.NotFound).CreateClient());

        var result = await client.UpdateAsync(3, new EmployeeInput(), CancellationToken.None);

        Assert.True(result.NotFound);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "tenant_required")]
    [InlineData(HttpStatusCode.BadRequest, "unknown_tenant")]
    public async Task TenantRejection_Throws(HttpStatusCode status, string code)
    {
        var problem = $$"""{"title":"No tenant.","status":{{(int)status}},"code":"{{code}}"}""";
        var client = new EmployeesApiClient(new StubHttpHandler(status, problem, "application/problem+json").CreateClient());

        var list = await Assert.ThrowsAsync<TenantSelectionRequiredException>(() => client.ListAsync(null, false, CancellationToken.None));
        var create = await Assert.ThrowsAsync<TenantSelectionRequiredException>(() => client.CreateAsync(new EmployeeInput(), CancellationToken.None));

        Assert.Equal(code, list.Code);
        Assert.Equal(code, create.Code);
    }

    [Fact]
    public async Task ServerError_Throws()
    {
        var client = new EmployeesApiClient(new StubHttpHandler(HttpStatusCode.InternalServerError).CreateClient());

        await Assert.ThrowsAsync<HttpRequestException>(() => client.ListSchedulesAsync(CancellationToken.None));
    }
}
