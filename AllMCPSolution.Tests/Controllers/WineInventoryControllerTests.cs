using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AllMCPSolution.Controllers;
using AllMCPSolution.Models;
using AllMCPSolution.Repositories;
using AllMCPSolution.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Moq;
using OpenAI.Chat;
using Xunit;

namespace AllMCPSolution.Tests.Controllers;

public class WineInventoryControllerTests
{
    [Fact]
    public async Task GetWineSurferMatches_ReturnsEmptyList_WhenQueryTooShort()
    {
        var bottleRepository = new Mock<IBottleRepository>();
        var bottleLocationRepository = new Mock<IBottleLocationRepository>();
        var wineRepository = new Mock<IWineRepository>();
        var wineVintageRepository = new Mock<IWineVintageRepository>();
        var subAppellationRepository = new Mock<ISubAppellationRepository>();
        var appellationRepository = new Mock<IAppellationRepository>();
        var regionRepository = new Mock<IRegionRepository>();
        var countryRepository = new Mock<ICountryRepository>();
        var userRepository = new Mock<IUserRepository>();
        var tastingNoteRepository = new Mock<ITastingNoteRepository>();
        var wineCatalogService = new Mock<IWineCatalogService>();
        var topBarService = new Mock<IWineSurferTopBarService>();
        var wineImportService = new Mock<IWineImportService>();
        var chatService = new Mock<IChatGptService>(MockBehavior.Strict);
        var userStore = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(userStore.Object, null, null, null, null, null, null, null, null);

        var controller = new WineInventoryController(
            bottleRepository.Object,
            bottleLocationRepository.Object,
            wineRepository.Object,
            wineVintageRepository.Object,
            subAppellationRepository.Object,
            appellationRepository.Object,
            regionRepository.Object,
            countryRepository.Object,
            userRepository.Object,
            tastingNoteRepository.Object,
            wineCatalogService.Object,
            topBarService.Object,
            wineImportService.Object,
            chatService.Object,
            userManager.Object);

        var result = await controller.GetWineSurferMatches("ab", CancellationToken.None);

        result.Should().BeOfType<JsonResult>();
        var json = (JsonResult)result;
        json.Value.Should().BeOfType<WineSurferLookupResponse>();
        var response = (WineSurferLookupResponse)json.Value!;
        response.Wines.Should().BeEmpty();

        chatService.Verify(service => service.GetChatCompletionAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<string?>(),
            It.IsAny<double?>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
