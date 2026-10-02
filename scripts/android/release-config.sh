#!/usr/bin/env bash

# shellcheck disable=SC2034 # Values are consumed by scripts that source this file.
# Shared TATAPP Android release identity. Keep these values synchronized with
# TATAPP.Android.csproj and Properties/AndroidManifest.xml; verify-apk.sh
# enforces them against every distributable.
readonly TATAPP_ANDROID_PACKAGE_ID='com.grayscaleconsultants.tatapp'
readonly TATAPP_ANDROID_VERSION_CODE='2'
readonly TATAPP_ANDROID_VERSION_NAME='0.3.1'
readonly TATAPP_ANDROID_ABIS='arm64-v8a,x86_64'
readonly TATAPP_ANDROID_ARTIFACT_BASENAME="TATAPP-${TATAPP_ANDROID_VERSION_NAME}-evaluation-arm64-x86_64.apk"
