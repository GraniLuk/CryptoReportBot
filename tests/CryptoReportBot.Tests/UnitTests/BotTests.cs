using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CryptoReportBot.Tests.UnitTests
{
    public class BotTests
    {
        [Fact]
        public void Bot_Initialization_ShouldCreateValidInstance()
        {
            // Arrange
            var configMock = new Mock<IConfigurationManager>();
            var loggerMock = new Mock<ILogger<Bot>>();
            var createAlertHandlerMock = new Mock<CreateAlertHandler>(
                Mock.Of<ILogger<CreateAlertHandler>>(),
                Mock.Of<IAzureFunctionsClient>());
            var createGmtAlertHandlerMock = new Mock<CreateGmtAlertHandler>(
                Mock.Of<ILogger<CreateGmtAlertHandler>>(),
                Mock.Of<IAzureFunctionsClient>());
            var removeAlertHandlerMock = new Mock<RemoveAlertHandler>(
                Mock.Of<IConfigurationManager>(),
                Mock.Of<ILogger<RemoveAlertHandler>>(),
                Mock.Of<IAzureFunctionsClient>());
            var listAlertsHandlerMock = new Mock<ListAlertsHandler>(
                Mock.Of<ILogger<ListAlertsHandler>>(),
                Mock.Of<IAzureFunctionsClient>());

            configMock.Setup(c => c.BotToken).Returns("test_token");

            // Act
            var bot = new Bot(
                configMock.Object,
                loggerMock.Object,
                createAlertHandlerMock.Object,
                createGmtAlertHandlerMock.Object,
                removeAlertHandlerMock.Object,
                listAlertsHandlerMock.Object
            );

            // Assert
            Assert.NotNull(bot);
            Assert.Empty(bot.UserStates);
        }
    }
}