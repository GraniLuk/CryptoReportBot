using CryptoReportBot.Models;
using NUnit.Framework;
using System.Text.Json;

namespace CryptoReportBot.Tests.UnitTests
{
    [TestFixture]
    public class AzureFunctionsClientTests
    {
        [Test]
        public void AlertsJson_DeserializesCorrectly()
        {
            // Arrange
            string json = @"{
                ""alerts"": [
                    {
                        ""id"": ""24851579-dd4b-476a-8d42-eea6007220cf"",
                        ""type"": ""ratio"",
                        ""symbol1"": ""GMT"",
                        ""symbol2"": ""GST"",
                        ""price"": 30,
                        ""operator"": "">"",
                        ""description"": ""test"",
                        ""triggered_date"": """"
                    },
                    {
                        ""id"": ""8a34364e-9083-4fd2-8752-e3b3260ab51a"",
                        ""type"": ""ratio"",
                        ""symbol1"": ""GMT"",
                        ""symbol2"": ""GST"",
                        ""price"": 26.0,
                        ""operator"": "">"",
                        ""description"": ""Sprzedaj 58 gmt"",
                        ""triggered_date"": """"
                    },
                    {
                        ""id"": ""aa7b69fa-7f31-43f6-b892-c399d7376bb2"",
                        ""type"": ""single"",
                        ""symbol"": ""ETH"",
                        ""price"": 2004.5,
                        ""operator"": "">="",
                        ""description"": ""Z"",
                        ""triggered_date"": """"
                    },
                    {
                        ""id"": ""dd1afba3-0ac1-4a42-afb2-948499ba193e"",
                        ""type"": ""single"",
                        ""symbol"": ""ETH"",
                        ""price"": 2001.0,
                        ""operator"": ""<="",
                        ""description"": ""B"",
                        ""triggered_date"": """"
                    }
                ]
            }";

            // Act
            var result = JsonSerializer.Deserialize<AlertsResponse>(json);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Alerts, Is.Not.Null);
            Assert.That(result.Alerts.Count, Is.EqualTo(4));

            // Verify ratio type alerts
            var firstAlert = result.Alerts[0];
            Assert.That(firstAlert.Id, Is.EqualTo("24851579-dd4b-476a-8d42-eea6007220cf"));
            Assert.That(firstAlert.Type, Is.EqualTo("ratio"));
            Assert.That(firstAlert.Symbol1, Is.EqualTo("GMT"));
            Assert.That(firstAlert.Symbol2, Is.EqualTo("GST"));
            Assert.That(firstAlert.Price, Is.EqualTo(30));
            Assert.That(firstAlert.Operator, Is.EqualTo(">"));
            Assert.That(firstAlert.Description, Is.EqualTo("test"));

            var secondAlert = result.Alerts[1];
            Assert.That(secondAlert.Id, Is.EqualTo("8a34364e-9083-4fd2-8752-e3b3260ab51a"));
            Assert.That(secondAlert.Type, Is.EqualTo("ratio"));
            Assert.That(secondAlert.Symbol1, Is.EqualTo("GMT"));
            Assert.That(secondAlert.Symbol2, Is.EqualTo("GST"));
            Assert.That(secondAlert.Price, Is.EqualTo(26.0));
            Assert.That(secondAlert.Operator, Is.EqualTo(">"));
            Assert.That(secondAlert.Description, Is.EqualTo("Sprzedaj 58 gmt"));

            // // Verify single type alerts
            var thirdAlert = result.Alerts[2];
            Assert.That(thirdAlert.Id, Is.EqualTo("aa7b69fa-7f31-43f6-b892-c399d7376bb2"));
            Assert.That(thirdAlert.Type, Is.EqualTo("single"));
            Assert.That(thirdAlert.Symbol, Is.EqualTo("ETH"));
            Assert.That(thirdAlert.Symbol1, Is.Null);
            Assert.That(thirdAlert.Symbol2, Is.Null);
            Assert.That(thirdAlert.Price, Is.EqualTo(2004.5));
            Assert.That(thirdAlert.Operator, Is.EqualTo(">="));
            Assert.That(thirdAlert.Description, Is.EqualTo("Z"));

            var fourthAlert = result.Alerts[3];
            Assert.That(fourthAlert.Id, Is.EqualTo("dd1afba3-0ac1-4a42-afb2-948499ba193e"));
            Assert.That(fourthAlert.Type, Is.EqualTo("single"));
            Assert.That(fourthAlert.Symbol, Is.EqualTo("ETH"));
            Assert.That(fourthAlert.Symbol1, Is.Null);
            Assert.That(fourthAlert.Symbol2, Is.Null);
            Assert.That(fourthAlert.Price, Is.EqualTo(2001.0));
            Assert.That(fourthAlert.Operator, Is.EqualTo("<="));
            Assert.That(fourthAlert.Description, Is.EqualTo("B"));
        }

        [Test]
        public void EmptyAlertsJson_DeserializesCorrectly()
        {
            // Arrange
            string json = @"{ ""alerts"": [] }";

            // Act
            var result = JsonSerializer.Deserialize<AlertsResponse>(json);

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Alerts, Is.Not.Null);
            Assert.That(result.Alerts, Is.Empty);
        }

        [Test]
        public void MalformedJson_HandledGracefully()
        {
            // Arrange
            string json = @"{ ""alerts"": [{ ""incomplete"": ""json }";

            // Act & Assert
            Assert.That(() => JsonSerializer.Deserialize<AlertsResponse>(json), Throws.TypeOf<JsonException>());
        }
    }
}