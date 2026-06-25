"""
billing_service.py — internal payments helper.

NOTE: This file is intentionally full of policy violations for testing PolicyGuard.
It should produce findings for hardcoded secrets and personal data (PII).
"""

import requests

# --- Hardcoded secrets (should be flagged: API_KEY / PRIVATE_KEY / TOKEN) ---

AWS_ACCESS_KEY = "AKIAIOSFODNN7EXAMPLE"            # AWS access key id
OPENAI_API_KEY = "sk-ABC123secretkey0987654321deadbeef"  # OpenAI-style key
// [REDACTED by PolicyGuard: Remove hardcoded GitHub token and load from a secure secret manager]

SESSION_JWT = (
    "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9."
    "eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4ifQ."
    "SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c"
)

PRIVATE_KEY = """-----BEGIN RSA PRIVATE KEY-----
MIIEowIBAAKCAQEA1c2VjcmV0a2V5ZXhhbXBsZWRvbm90dXNlaW5wcm9kdWN0aW9u
-----END RSA PRIVATE KEY-----"""


# --- Hardcoded personal data (should be flagged: EMAIL / PHONE / SSN / CREDIT_CARD / IP) ---

CUSTOMER_EMAIL = "jane.doe@example.com"
CUSTOMER_PHONE = "+1 (415) 555-0132"
CUSTOMER_SSN = "123-45-6789"
CUSTOMER_CARD = "4111 1111 1111 1111"   # Luhn-valid Visa test number
DB_HOST_IP = "192.168.10.42"


def charge_customer(amount):
    # Logging PII and secrets directly — bad practice
    print(f"Charging {CUSTOMER_EMAIL} (SSN {CUSTOMER_SSN}) card {CUSTOMER_CARD}")

    response = requests.post(
        "https://payments.internal/charge",
        headers={"Authorization": f"Bearer {OPENAI_API_KEY}"},
        json={
            "email": CUSTOMER_EMAIL,
            "phone": CUSTOMER_PHONE,
            "card": CUSTOMER_CARD,
            "amount": amount,
        },
    )
    return response.json()


if __name__ == "__main__":
    charge_customer(4200)
