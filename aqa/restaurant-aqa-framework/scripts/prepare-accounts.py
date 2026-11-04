#!/usr/bin/env python3
"""Check dedicated accounts and optionally assign the test waiter through the app API.

Uses only Python's standard library. Credentials stay in memory and are never printed.
"""

import argparse
import json
import os
from pathlib import Path
import sys
from urllib.error import HTTPError, URLError
from urllib.parse import quote, urlsplit
from urllib.request import Request, urlopen


class SetupError(Exception):
    pass


def field(value, name):
    if not isinstance(value, dict):
        raise SetupError("Unexpected API response: expected an object.")
    for key, result in value.items():
        if key.casefold() == name.casefold():
            return result
    raise SetupError("API response is missing the expected field: " + name)


def read_configuration():
    path = Path(__file__).resolve().parents[1] / ".env"
    values = {}
    if path.exists():
        for line in path.read_text().splitlines():
            line = line.strip()
            if not line or line.startswith("#"):
                continue
            key, separator, value = line.partition("=")
            if not separator:
                raise SetupError("Expected KEY=value in the private .env file.")
            value = value.strip()
            if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
                value = value[1:-1]
            values[key.strip()] = value
    values.update({key: value for key, value in os.environ.items() if key.startswith("AQA_")})
    required = ["AQA_API_BASE_URL"]
    for role in ("OWNER", "OTHER_CUSTOMER", "WAITER"):
        required.extend(["AQA_" + role + "_EMAIL", "AQA_" + role + "_PASSWORD"])
    missing = [key for key in required if not values.get(key)]
    if missing:
        raise SetupError("Fill these private configuration fields: " + ", ".join(missing))
    emails = [values["AQA_" + role + "_EMAIL"].strip().casefold()
              for role in ("OWNER", "OTHER_CUSTOMER", "WAITER")]
    if len(set(emails)) != 3:
        raise SetupError("Owner, other customer, and waiter must use three distinct accounts.")
    parsed = urlsplit(values["AQA_API_BASE_URL"])
    if (parsed.scheme != "https" or not parsed.hostname or parsed.username or parsed.password
            or parsed.query or parsed.fragment):
        raise SetupError("AQA_API_BASE_URL must be an HTTPS base URL without credentials, query, or fragment.")
    return values


def request(base_url, path, token=None, payload=None, allowed=(200,)):
    headers = {"Accept": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    data = None
    if payload is not None:
        headers["Content-Type"] = "application/json"
        data = json.dumps(payload).encode()
    req = Request(base_url.rstrip("/") + "/" + path, data=data, headers=headers)
    try:
        with urlopen(req, timeout=30) as response:
            body = response.read()
            status = response.status
    except HTTPError as error:
        status = error.code
        body = error.read()
    except (URLError, TimeoutError, OSError):
        raise SetupError("Cannot reach the API for " + path + ". Check network access and the deployed URL.") from None
    if status not in allowed:
        raise SetupError("API request to " + path + " returned HTTP " + str(status)
                         + ". No credentials or response body have been logged.")
    try:
        return status, json.loads(body) if body else None
    except (ValueError, UnicodeError):
        raise SetupError("API returned invalid JSON for " + path) from None


def sign_in(config, role):
    _, tokens = request(config["AQA_API_BASE_URL"], "signin", payload={
        "email": config["AQA_" + role + "_EMAIL"],
        "password": config["AQA_" + role + "_PASSWORD"],
    })
    token = field(tokens, "idToken")
    if not isinstance(token, str) or not token:
        raise SetupError(role + " sign-in returned no usable ID token.")
    _, user = request(config["AQA_API_BASE_URL"], "me", token=token)
    name = str(field(user, "firstName")) + " " + str(field(user, "lastName"))
    print(role + ": signed in as " + name + "; role " + str(field(user, "role")))
    return token, user


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--check", action="store_true", help="Read-only account and location check")
    mode.add_argument("--promote-waiter", action="store_true", help="Assign only the configured waiter account")
    parser.add_argument("--location-id", help="Explicit restaurant location for waiter assignment")
    args = parser.parse_args()
    if args.promote_waiter and not args.location_id:
        parser.error("--promote-waiter requires --location-id")

    config = read_configuration()
    base = config["AQA_API_BASE_URL"]
    identities = {}
    for role in ("OWNER", "OTHER_CUSTOMER", "WAITER"):
        try:
            identities[role] = sign_in(config, role)
        except SetupError as error:
            raise SetupError(role + ": " + str(error)) from None
    for role in ("OWNER", "OTHER_CUSTOMER"):
        if str(field(identities[role][1], "role")).casefold() != "customer":
            raise SetupError(role + " must have the Customer role.")
    if len({field(identity[1], "id") for identity in identities.values()}) != 3:
        raise SetupError("The configured accounts do not resolve to three distinct users.")

    _, locations = request(base, "locations")
    if not isinstance(locations, list) or not locations:
        raise SetupError("The deployed application returned no restaurant locations.")
    print("\nAvailable restaurant locations:")
    location_ids = set()
    for location in locations:
        location_id = str(field(location, "id"))
        location_ids.add(location_id)
        print("  " + location_id + " — " + str(field(location, "address")))

    waiter_token, waiter = identities["WAITER"]
    waiter_path = "waiters/" + quote(str(field(waiter, "id")), safe="") + "/location"
    status, assignment = request(base, waiter_path, token=waiter_token, allowed=(200, 404))
    if status == 200:
        existing_location = str(field(assignment, "locationId"))
        print("\nConfigured waiter location: " + existing_location)
        if args.promote_waiter and existing_location != args.location_id:
            raise SetupError("The waiter already belongs to another location; existing assignment was preserved.")
    else:
        print("\nConfigured waiter has no restaurant assignment yet.")

    if args.check:
        print("Check complete. No account roles or location assignments were changed.")
        return
    if args.location_id not in location_ids:
        raise SetupError("The requested location does not exist. No changes were made.")
    if status == 404:
        request(base, "waiters", token=waiter_token, payload={
            "email": config["AQA_WAITER_EMAIL"], "locationId": args.location_id,
        })
        print("Waiter role and restaurant assignment requested through the application API.")

    # Reauthenticate after changing groups so the JWT contains the current role.
    waiter_token, waiter = sign_in(config, "WAITER")
    if str(field(waiter, "role")).casefold() != "waiter":
        raise SetupError("The account does not yet report the Waiter role. Check the backend assignment.")
    _, assignment = request(base, waiter_path, token=waiter_token)
    if str(field(assignment, "locationId")) != args.location_id:
        raise SetupError("Waiter location verification failed.")
    print("Waiter setup verified. Sign out and in again if this account is open in your browser.")


if __name__ == "__main__":
    try:
        main()
    except SetupError as error:
        print("Setup failed: " + str(error), file=sys.stderr)
        sys.exit(1)
