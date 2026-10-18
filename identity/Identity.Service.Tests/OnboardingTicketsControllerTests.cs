using Identity.Service.Controllers;
using Identity.Service.DTOs;
using Identity.Service.Exceptions;
using Identity.Service.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Xunit;

namespace Identity.Service.Tests;

public sealed class OnboardingTicketsControllerTests
{
    [Fact]
    public async Task Create_RejectsFailedCaptcha()
    {
        var tickets = new Mock<IOnboardingTicketService>();
        var captcha = new Mock<ITurnstileVerifier>();
        captcha.Setup(item => item.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var controller = CreateController(tickets, captcha);

        var result = await controller.Create(new CreateOnboardingTicketRequest(), CancellationToken.None);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result.Result).StatusCode);
        tickets.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Create_ReturnsCreatedOrConflict()
    {
        var tickets = new Mock<IOnboardingTicketService>();
        var captcha = new Mock<ITurnstileVerifier>();
        captcha.Setup(item => item.VerifyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var response = new CreateOnboardingTicketResponse { TicketId = Guid.NewGuid(), Status = "PENDING" };
        tickets.Setup(item => item.CreateAsync(It.IsAny<CreateOnboardingTicketRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var controller = CreateController(tickets, captcha);

        Assert.Equal(StatusCodes.Status201Created,
            Assert.IsType<ObjectResult>((await controller.Create(new(), CancellationToken.None)).Result).StatusCode);

        tickets.Setup(item => item.CreateAsync(It.IsAny<CreateOnboardingTicketRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DuplicateEmailException("duplicate@example.com"));
        Assert.IsType<ConflictObjectResult>(
            (await controller.Create(new(), CancellationToken.None)).Result);
    }

    private static OnboardingTicketsController CreateController(
        Mock<IOnboardingTicketService> tickets,
        Mock<ITurnstileVerifier> captcha) =>
        new(tickets.Object, captcha.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
}

public sealed class AdminOnboardingTicketsControllerTests
{
    [Fact]
    public async Task ReviewEndpointsUseAuthenticatedReviewer()
    {
        var service = new Mock<IOnboardingTicketService>();
        var id = Guid.NewGuid();
        service.Setup(item => item.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        service.Setup(item => item.ApproveAsync(id, "admin@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OnboardingTicketResponse());
        service.Setup(item => item.RejectAsync(id, "admin@example.com", "reason", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OnboardingTicketResponse());
        var controller = new AdminOnboardingTicketsController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new System.Security.Claims.ClaimsPrincipal(
                        new System.Security.Claims.ClaimsIdentity(
                            [new System.Security.Claims.Claim(
                                System.Security.Claims.ClaimTypes.Email, "admin@example.com")], "test"))
                }
            }
        };

        Assert.IsType<OkObjectResult>((await controller.GetAll(CancellationToken.None)).Result);
        Assert.IsType<OkObjectResult>((await controller.Approve(id, CancellationToken.None)).Result);
        Assert.IsType<OkObjectResult>((await controller.Reject(
            id, new RejectOnboardingTicketRequest { Reason = "reason" }, CancellationToken.None)).Result);
    }
}
