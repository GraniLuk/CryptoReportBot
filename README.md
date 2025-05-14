# Crypto Report Bot

A Telegram bot that provides a user-friendly interface for managing cryptocurrency price alerts. This bot serves as the frontend for [CryptoPriceAlerts](https://github.com/GraniLuk/CryptoPriceAlerts) Azure Function.

## Overview

This bot allows users to:
- Create price alerts for single cryptocurrencies
- Create ratio alerts for crypto pairs (e.g., GMT/GST)
- List all active alerts
- Remove existing alerts
- Receive notifications when price conditions are met

## Dependencies

- Python 3.7+
- aiohttp
- Azure Function backend ([CryptoPriceAlerts](https://github.com/GraniLuk/CryptoPriceAlerts))

## Setup

1. Clone this repository
2. Install required Python packages:
   ```bash
   pip install -r requirements.txt
   ```
3. Set up the following secrets:
   - `alerts_bot_token`: Your Telegram Bot token
   - `azure_function_url`: URL to your deployed CryptoPriceAlerts Azure Function
   - `azure_function_key`: Access key for your Azure Function

## Usage

### Available Commands

- `/createalert` - Start creating a new price alert for any cryptocurrency
- `/creategmtalert` - Create a GMT/GST ratio alert
- `/getalerts` - List all active alerts
- `/removealert` - Remove an existing alert

### Creating an Alert

1. Start with `/createalert` command
2. Select or type a cryptocurrency symbol
3. Choose an operator (>, <, >=, <=)
4. Enter the target price
5. Add a description for your alert

## Related Projects

This bot requires [CryptoPriceAlerts](https://github.com/GraniLuk/CryptoPriceAlerts) Azure Function to be set up and running. The Azure Function handles:
- Alert storage
- Price monitoring
- Notification triggers

Make sure to deploy the Azure Function before using this bot.

## License

[MIT License](LICENSE)
