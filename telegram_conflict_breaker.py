#!/usr/bin/env python3
"""
Emergency Telegram Bot Conflict Breaker
This script aggressively breaks any existing getUpdates connections for a Telegram bot
by repeatedly consuming updates until no conflicts exist.
"""

import requests
import time
import json
import sys
import os
from datetime import datetime

def get_bot_token():
    """Get bot token from environment or user input"""
    token = os.getenv('BOT_TOKEN')
    if not token:
        token = input("Enter your Telegram Bot Token: ").strip()
    return token

def make_telegram_request(token, method, params=None):
    """Make a request to Telegram API"""
    url = f"https://api.telegram.org/bot{token}/{method}"
    try:
        if params:
            response = requests.post(url, json=params, timeout=30)
        else:
            response = requests.get(url, timeout=30)
        return response
    except Exception as e:
        print(f"❌ Request failed: {e}")
        return None

def break_bot_conflicts(token, max_attempts=20):
    """Aggressively break any existing bot polling conflicts"""
    print(f"🚀 Starting aggressive bot conflict resolution...")
    print(f"📊 Target: Breaking all existing getUpdates connections")
    print(f"⏰ Started at: {datetime.now()}")
    print("=" * 60)
    
    # Step 1: Delete any webhooks
    print("🧹 Step 1: Deleting webhooks...")
    webhook_response = make_telegram_request(token, "deleteWebhook", {"drop_pending_updates": True})
    if webhook_response and webhook_response.status_code == 200:
        print("✅ Webhooks deleted successfully")
    else:
        print("❌ Failed to delete webhooks")
    
    # Step 2: Aggressively consume updates to break polling
    print("\n🔥 Step 2: Aggressively consuming updates to break existing polling...")
    
    for attempt in range(1, max_attempts + 1):
        print(f"\n--- Attempt {attempt}/{max_attempts} ---")
        
        # Try to get updates with very short timeout
        params = {
            "timeout": 1,  # Very short timeout
            "limit": 100   # Get many updates at once
        }
        
        print(f"📡 Making getUpdates request (timeout=1s)...")
        start_time = time.time()
        
        response = make_telegram_request(token, "getUpdates", params)
        elapsed = time.time() - start_time
        
        if response is None:
            print(f"❌ Request failed (network error)")
            continue
            
        print(f"📈 Response: {response.status_code} (took {elapsed:.2f}s)")
        
        if response.status_code == 200:
            try:
                data = response.json()
                updates = data.get('result', [])
                print(f"✅ SUCCESS! Got {len(updates)} updates, no conflict detected!")
                
                if updates:
                    # Acknowledge the updates by getting them with offset
                    last_update_id = max(update['update_id'] for update in updates)
                    ack_params = {"offset": last_update_id + 1, "timeout": 1}
                    ack_response = make_telegram_request(token, "getUpdates", ack_params)
                    print(f"📤 Acknowledged updates up to ID {last_update_id}")
                
                print(f"🎉 Bot conflict resolved after {attempt} attempts!")
                return True
                
            except Exception as e:
                print(f"❌ Failed to parse response: {e}")
                
        elif response.status_code == 409:
            print(f"🔄 Still conflicting (409), continuing...")
            # Wait a bit before trying again
            time.sleep(2)
            
        else:
            print(f"❌ Unexpected status: {response.status_code}")
            if hasattr(response, 'text'):
                print(f"   Response: {response.text[:200]}")
    
    print(f"\n❌ Failed to resolve conflicts after {max_attempts} attempts")
    return False

def main():
    print("🤖 Telegram Bot Conflict Breaker")
    print("================================")
    
    token = get_bot_token()
    if not token:
        print("❌ No bot token provided")
        sys.exit(1)
    
    # Get bot info first
    print("🔍 Getting bot info...")
    me_response = make_telegram_request(token, "getMe")
    if me_response and me_response.status_code == 200:
        try:
            bot_info = me_response.json()['result']
            print(f"🤖 Bot: {bot_info['first_name']} (@{bot_info.get('username', 'N/A')})")
        except:
            print("❓ Could not get bot info")
    
    # Start conflict resolution
    success = break_bot_conflicts(token)
    
    if success:
        print("\n🎉 SUCCESS! Bot conflicts have been resolved!")
        print("✨ Your bot should now be able to start polling without conflicts.")
    else:
        print("\n❌ FAILED! Could not resolve bot conflicts.")
        print("💡 Manual intervention may be required.")
    
    print(f"\n⏰ Finished at: {datetime.now()}")

if __name__ == "__main__":
    main()
