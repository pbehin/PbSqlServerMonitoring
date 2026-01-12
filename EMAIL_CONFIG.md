# Email Configuration Guide

## Development Setup with Gmail (Recommended)

Gmail is free and straightforward to set up.

### Step 1: Create/Use Gmail Account
Use an existing Gmail account or create a new one at https://gmail.com

### Step 2: Enable 2FA and Get App Password

1. Sign in to your Google Account at https://myaccount.google.com
2. Go to **Security** → **App passwords**
3. Select "Mail" and "Windows Computer"
4. Google generates a 16-character password - copy it (with or without spaces)

### Step 3: Update appsettings.Development.json

Add this configuration to your `appsettings.Development.json`:

```json
{
  "Email": {
    "SmtpHost": "smtp.gmail.com",
    "SmtpPort": "587",
    "SmtpUser": "your-email@gmail.com",
    "SmtpPassword": "xxxx xxxx xxxx xxxx",
    "FromEmail": "your-email@gmail.com",
    "FromName": "SQL Server Monitor",
    "AppName": "SQL Server Monitor"
  }
}
```

Replace with your actual Gmail address and app password.

### Step 4: Test Confirmation Emails

Registration will now send real confirmation emails to the user's inbox. They can click the link to verify their account.

---

## Alternative: Ethereal Email (Fake Testing Service)

Ethereal is useful if you don't want to send real emails.

1. Go to https://ethereal.email
2. Click "Create Ethereal Account"
3. Copy credentials and configure:

```json
{
  "Email": {
    "SmtpHost": "smtp.ethereal.email",
    "SmtpPort": "587",
    "SmtpUser": "your-ethereal-user@ethereal.email",
    "SmtpPassword": "your-ethereal-password",
    "FromEmail": "your-ethereal-user@ethereal.email",
    "FromName": "SQL Server Monitor",
    "AppName": "SQL Server Monitor"
  }
}
```

---

## Alternative: Outlook.com (Free)

```json
{
  "Email": {
    "SmtpHost": "smtp.office365.com",
    "SmtpPort": "587",
    "SmtpUser": "your-email@outlook.com",
    "SmtpPassword": "your-password",
    "FromEmail": "your-email@outlook.com",
    "FromName": "SQL Server Monitor",
    "AppName": "SQL Server Monitor"
  }
}
```

---

## Testing the Configuration

Once configured, registration will:
1. Create the user account
2. Generate an email confirmation token
3. Send a confirmation email via your configured SMTP service
4. User must click the confirmation link before logging in

For Gmail: Check the recipient's inbox for the confirmation email.
For Ethereal: Check the preview URL shown after sending to verify the email content.
