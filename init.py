import logging
from telegram import (ReplyKeyboardMarkup, ReplyKeyboardRemove, Update,
                      InlineKeyboardButton, InlineKeyboardMarkup)
from telegram.ext import (Application, CallbackQueryHandler, CommandHandler,
                          ContextTypes, ConversationHandler, MessageHandler, filters)
import aiohttp
from typing import Dict, Any
from config import get_secret
import locale
from telegram.error import Conflict
import sys
import logging

# Enable logging
logging.basicConfig(format='%(asctime)s - %(name)s - %(levelname)s - %(message)s',
                    level=logging.INFO)

logger = logging.getLogger(__name__)

# Define states
CRYPTO_DESCRIPTION, SUMMARY, CRYPTO_SYMBOL, CRYPTO_OPERATOR = range(4)

# Replace hyphens with underscores in secret names
BOT_TOKEN = get_secret("alerts-bot-token")
AZURE_FUNCTION_URL = get_secret("azure-function-url")
AZURE_FUNCTION_KEY = get_secret("azure-function-key")

# Set locale to a known format
try:
    locale.setlocale(locale.LC_NUMERIC, 'en_US.UTF-8')
except locale.Error:
    locale.setlocale(locale.LC_NUMERIC, 'C')

# Assert that configuration values are fetched properly
assert BOT_TOKEN is not None, "BOT_TOKEN is not set"
assert AZURE_FUNCTION_URL is not None, "AZURE_FUNCTION_URL is not set"
assert AZURE_FUNCTION_KEY is not None, "AZURE_FUNCTION_KEY is not set"

async def createalert(update: Update, context: ContextTypes.DEFAULT_TYPE) -> int:
    """Starts the conversation and asks the user about which cryptocurrency they want to use."""
    reply_keyboard = [['BTC', 'DOT', 'BNB', 'MATIC', 'FLOW', 'ATOM', 'OSMO', 'ETH', 'HBAR']]

    await update.message.reply_text(
        '<b>Welcome to the Crypto Report Bot!\n'
        'I will help you to create alert for your specific cryptocurrency.\n'
        'What is crypto symbol for new alert?</b>',
        parse_mode='HTML',
        reply_markup=ReplyKeyboardMarkup(reply_keyboard, one_time_keyboard=True, resize_keyboard=True),
    )

    return CRYPTO_SYMBOL


async def crypto_symbol(update: Update, context: ContextTypes.DEFAULT_TYPE) -> int:
    """Stores the user's crypto symbol."""
    user = update.message.from_user
    context.user_data['symbol'] = update.message.text
    logger.info('Crypto symbol of %s: %s', user.first_name, update.message.text)
    await update.message.reply_text(
        f'<b>You selected {update.message.text} crypto.\n'
        f'What operator do you want?</b>',
        parse_mode='HTML',
        reply_markup=ReplyKeyboardRemove(),
    )

    # Define inline buttons for operator selection
    keyboard = [
        [InlineKeyboardButton('Greater', callback_data='greater')],
        [InlineKeyboardButton('Lower', callback_data='lower')],
        [InlineKeyboardButton('Greater or equal', callback_data='greater_or_equal')],
        [InlineKeyboardButton('Lower or equal', callback_data='lower_or_equal')],
    ]
    reply_markup = InlineKeyboardMarkup(keyboard)
    await update.message.reply_text('<b>Please choose:</b>', parse_mode='HTML', reply_markup=reply_markup)

    return CRYPTO_OPERATOR


async def crypto_operator(update: Update, context: ContextTypes.DEFAULT_TYPE) -> int:
    """Stores the selected operator."""
    query = update.callback_query
    await query.answer()
    
    # Map callback_data to operators
    operator_map = {
        'greater': '&gt;',  # HTML escaped >
        'lower': '&lt;',    # HTML escaped <
        'greater_or_equal': '&gt;=',  # HTML escaped >=
        'lower_or_equal': '&lt;='     # HTML escaped <=
    }
    
    context.user_data['operator'] = operator_map[query.data].replace('&gt;', '>').replace('&lt;', '<')
    await query.message.reply_text(
        f'<b>You selected {operator_map[query.data]} operator.\n'
        f'What price level do you want to set for {context.user_data["symbol"]}?</b>',
        parse_mode='HTML'
    )
    
    return CRYPTO_DESCRIPTION


async def crypto_description(update: Update, context: ContextTypes.DEFAULT_TYPE) -> int:
    """Stores the alert description."""
    context.user_data['price'] = update.message.text
    await update.message.reply_text('<b>Price noted.\n'
                                    'Please add description:</b>',
                                    parse_mode='HTML')
    return SUMMARY


async def send_alert_request(data: Dict[str, Any]) -> bool:
    """Sends alert data to Azure Function."""
    url = AZURE_FUNCTION_URL
    params = {
        "code": AZURE_FUNCTION_KEY
    }
    
    try:
        async with aiohttp.ClientSession() as session:
            # Log the JSON payload
            logger.info(f"Sending alert data: {data}")
            async with session.post(
                url,
                params=params,
                json=data,
                headers={"Content-Type": "application/json"}
            ) as response:
                response_text = await response.text()
                logger.info(f"Response status: {response.status}, Response text: {response_text}")
                return response.status == 200
    except Exception as e:
        logger.error(f"Error sending alert: {e}")
        return False

async def get_all_alerts() -> list:
    """Fetches all alerts from Azure Function."""
    # Construct the URL by replacing 'insert_new_alert_grani' with 'get_all_alerts'
    url = AZURE_FUNCTION_URL.replace('insert_new_alert_grani', 'get_all_alerts')
    params = {
        "code": AZURE_FUNCTION_KEY
    }
    
    try:
        async with aiohttp.ClientSession() as session:
            async with session.get(
                url,
                params=params,
                headers={"Content-Type": "application/json"}
            ) as response:
                if response.status == 200:
                    return await response.json()
                else:
                    logger.error(f"Error getting alerts: {response.status}")
                    return []
    except Exception as e:
        logger.error(f"Error fetching alerts: {e}")
        return []

async def list_alerts(update: Update, context: ContextTypes.DEFAULT_TYPE) -> None:
    """Handler for /listalerts command."""
    alerts = await get_all_alerts()
    if not alerts:
        await update.message.reply_text("No alerts found or error fetching alerts.")
        return

    message = "<b>Current Alerts:</b>\n\n"
    for alert in alerts:
        message += f"Symbol: {alert['symbol']}\n"
        message += f"Price: {alert['price']}\n"
        message += f"Operator: {alert['operator']}\n"
        message += f"Description: {alert['description']}\n"
        message += "-------------------\n"

    await update.message.reply_text(message, parse_mode='HTML')

async def summary(update: Update, context: ContextTypes.DEFAULT_TYPE) -> int:
    """Stores the description."""
    context.user_data['description'] = update.message.text
    # Inform user and transition to summary
    await update.message.reply_text('<b>Description added successfully.\n'
                                    'Let\'s summarize your selections.</b>',
                                    parse_mode='HTML'
    )
    """Summarizes the user's selections and sends alert."""
    symbol = context.user_data.get('symbol')
    operator = context.user_data.get('operator')
    price = context.user_data.get('price')
    description = context.user_data.get('description')

    # Check if any required value is missing
    if not all([symbol, operator, price, description]):
        await update.message.reply_text("❌ Missing required information. Please start the process again.")
        return ConversationHandler.END

    await update.message.reply_text(
        f'<b>Summary:</b>\n'
        f'Symbol: {symbol}\n'
        f'Operator: {operator}\n'
        f'Price: {price}\n'
        f'Description: {description}',
        parse_mode='HTML'
    )

    # Prepare data for the alert
    try:
        # Try to convert using locale
        price_float = float(locale.atof(price))
    except ValueError:
        # Fallback: Replace comma with dot and try again
        try:
            price_float = float(price.replace(',', '.'))
        except ValueError as e:
            await update.message.reply_text(f"❌ Invalid price format: {e}")
            return ConversationHandler.END

    alert_data = {
        "symbol": symbol,
        "price": price_float,
        "operator": operator,
        "description": description
    }

    # Log the alert data for debugging
    logger.info(f"Alert data: {alert_data}")

    # Send the alert
    success = await send_alert_request(alert_data)
    
    if success:
        await update.message.reply_text("✅ Alert has been created successfully!")
    else:
        await update.message.reply_text("❌ Failed to create alert. Please try again later.")

    return ConversationHandler.END


async def cancel(update: Update, context: ContextTypes.DEFAULT_TYPE) -> int:
    """Cancels and ends the conversation."""
    await update.message.reply_text('Bye! Hope to talk to you again soon.', reply_markup=ReplyKeyboardRemove())
    return ConversationHandler.END

async def error_handler(update: Update, context: ContextTypes.DEFAULT_TYPE):
    """Handle errors caused by updates."""
    logging.error(f"Update {update} caused error {context.error}")
    if isinstance(context.error, Conflict):
        logging.error("Another bot instance is running. Shutting down...")
        await context.application.stop()
        sys.exit(1)

def main() -> None:
    """Run the bot."""
    application = Application.builder().token(BOT_TOKEN).build()
    
    # Add error handler
    application.add_error_handler(error_handler)

    conv_handler = ConversationHandler(
        entry_points=[CommandHandler('createalert', createalert)],
        states={
            CRYPTO_SYMBOL: [MessageHandler(filters.TEXT & ~filters.COMMAND, crypto_symbol)],
            CRYPTO_OPERATOR: [CallbackQueryHandler(crypto_operator)],
            CRYPTO_DESCRIPTION: [MessageHandler(filters.TEXT & ~filters.COMMAND, crypto_description)],
            SUMMARY: [MessageHandler(filters.ALL, summary)]
        },
        fallbacks=[CommandHandler('cancel', cancel)],
    )

    application.add_handler(conv_handler)

    # Handle the case when a user sends /createalert but they're not in a conversation
    application.add_handler(CommandHandler('createalert', createalert))
    application.add_handler(CommandHandler('getalerts', list_alerts))
    
    # Start the bot
    try:
        application.run_polling(allowed_updates=Update.ALL_TYPES)
    except Exception as e:
        logging.error(f"Error running bot: {e}")
        sys.exit(1)

# Before running, ensure no other instances are running
if __name__ == '__main__':
    logging.basicConfig(
        format='%(asctime)s - %(name)s - %(levelname)s - %(message)s',
        level=logging.INFO
    )
    main()