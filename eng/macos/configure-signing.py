#!/usr/bin/env python3
"""Import optional CI signing credentials into a disposable, scoped keychain.

Never exports local signing keys. With no certificate secrets, packaging uses
ad-hoc signatures. Run --cleanup in an always() Actions step.
"""
import base64
import json
import os
import pathlib
import re
import secrets
import shlex
import subprocess
import sys
import uuid


def run(*arguments, capture=False, check=True):
    return subprocess.run([str(value) for value in arguments], check=check, text=True,
                          stdout=subprocess.PIPE if capture else None)


def export(name, value):
    if "\n" in value or "\r" in value:
        raise ValueError(f"Invalid multiline value for {name}")
    with open(os.environ["GITHUB_ENV"], "a") as stream:
        stream.write(f"{name}={value}\n")


def main():
    temporary = pathlib.Path(os.environ["RUNNER_TEMP"])
    state_path = temporary / "lightdl-macos-signing.json"
    if "--cleanup" in sys.argv:
        if state_path.is_file():
            state = json.loads(state_path.read_text())
            run("security", "list-keychains", "-d", "user", "-s", *state["previous"])
            run("security", "delete-keychain", state["keychain"], check=False)
            state_path.unlink()
        return
    certificate = os.environ.get("MACOS_CERTIFICATE_P12_BASE64", "")
    identity = os.environ.get("MACOS_SIGN_IDENTITY", "")
    notary = [os.environ.get(name, "") for name in
              ("APPLE_ID", "APPLE_TEAM_ID", "APPLE_APP_SPECIFIC_PASSWORD")]
    if any(notary) and not all(notary):
        raise ValueError("Set all three Apple notarization secrets, or leave all three unset.")
    if not certificate:
        if identity or any(notary) or os.environ.get("MACOS_CERTIFICATE_PASSWORD"):
            raise ValueError("Certificate/identity/notarization configuration is incomplete: MACOS_CERTIFICATE_P12_BASE64 is required.")
        print("No Developer ID certificate configured; .app and .dmg will be automatically ad-hoc signed.")
        return
    keychain = temporary / f"lightdl-signing-{uuid.uuid4().hex}.keychain-db"
    previous = shlex.split(run("security", "list-keychains", "-d", "user", capture=True).stdout)
    state_path.write_text(json.dumps({"keychain": str(keychain), "previous": previous}))
    password = secrets.token_urlsafe(32)
    print(f"::add-mask::{password}", flush=True)
    run("security", "create-keychain", "-p", password, keychain)
    run("security", "set-keychain-settings", "-lut", "21600", keychain)
    run("security", "unlock-keychain", "-p", password, keychain)
    p12 = temporary / f"lightdl-certificate-{uuid.uuid4().hex}.p12"
    try:
        p12.write_bytes(base64.b64decode("".join(certificate.split()), validate=True))
        p12.chmod(0o600)
        run("security", "import", p12, "-P", os.environ.get("MACOS_CERTIFICATE_PASSWORD", ""),
            "-k", keychain, "-T", "/usr/bin/codesign", "-T", "/usr/bin/security")
    finally:
        p12.unlink(missing_ok=True)
    run("security", "set-key-partition-list", "-S", "apple-tool:,apple:,codesign:", "-s", "-k", password, keychain)
    run("security", "list-keychains", "-d", "user", "-s", keychain, *previous)
    available = run("security", "find-identity", "-v", "-p", "codesigning", keychain, capture=True).stdout
    candidates = re.findall(r'([0-9A-F]{40}) "(Developer ID Application:[^"\n]+)"', available)
    if identity:
        candidates = [(digest, name) for digest, name in candidates if identity in (digest, name)]
    if len(candidates) != 1:
        raise ValueError("Expected exactly one matching Developer ID Application certificate in the imported keychain.")
    export("MACOS_SIGN_IDENTITY", candidates[0][0])
    export("MACOS_SIGN_KEYCHAIN", str(keychain))
    if all(notary):
        profile = "lightdl-notary"
        run("xcrun", "notarytool", "store-credentials", profile, "--apple-id", notary[0],
            "--team-id", notary[1], "--password", notary[2], "--keychain", keychain)
        export("MACOS_NOTARY_PROFILE", profile)
    print("Developer ID signing configured in a temporary keychain.")


if __name__ == "__main__":
    main()
