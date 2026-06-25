"""
billing_service_secure.py — internal payments helper (hardened).

This is the "good" counterpart to billing_service.py: secrets come from the
environment, personal data is never hardcoded or logged, and customer details
are passed in at runtime rather than embedded in source.

PolicyGuard should find little or nothing here, yielding a high compliance score.
"""

import os
import logging

import requests

logger = logging.getLogger(__name__)

# --- Secrets are loaded from the environment, never hardcoded ---

PAYMENTS_API_KEY = os.environ["PAYMENTS_API_KEY"]
PAYMENTS_URL = os.environ.get("PAYMENTS_URL", "https://payments.internal/charge")


def _mask(value: str) -> str:
    """Return a non-sensitive, log-safe representation of an identifier."""
    if not value:
        return "<empty>"
    return f"***{value[-4:]}"


def charge_customer(customer, amount):
    """Charge a customer.

    `customer` is a mapping supplied by the caller (e.g. loaded from a secured
    datastore). No personal data is hardcoded or written to logs.
    """
    # Log only non-identifying, masked information.
    logger.info("Charging customer %s for amount %s", _mask(customer["id"]), amount)

    response = requests.post(
        PAYMENTS_URL,
        headers={"Authorization": f"Bearer {PAYMENTS_API_KEY}"},
        json={
            "customer_id": customer["id"],
            "amount": amount,
        },
        timeout=10,
    )
    response.raise_for_status()
    return response.json()


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO)
    # Caller provides customer data at runtime; nothing sensitive lives in source.
    demo_customer = {"id": os.environ.get("DEMO_CUSTOMER_ID", "unknown")}
    charge_customer(demo_customer, amount=4200)
