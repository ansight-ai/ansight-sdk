"use strict";

const DEFAULT_CAMERA_PERMISSION =
  "Allow $(PRODUCT_NAME) to use the camera to scan an Ansight host enrollment QR code.";
const DEFAULT_LOCAL_NETWORK_PERMISSION =
  "Allow $(PRODUCT_NAME) to connect to Ansight host on your local network during development.";

function withAnsightReactNative(config, options = {}) {
  const { withInfoPlist } = loadExpoConfigPlugins();

  return withInfoPlist(config, (modConfig) => {
    const infoPlist = modConfig.modResults;

    applyPermission(
      infoPlist,
      "NSCameraUsageDescription",
      options.cameraPermission,
      DEFAULT_CAMERA_PERMISSION,
    );
    applyPermission(
      infoPlist,
      "NSLocalNetworkUsageDescription",
      options.localNetworkPermission,
      DEFAULT_LOCAL_NETWORK_PERMISSION,
    );

    return modConfig;
  });
}

function loadExpoConfigPlugins() {
  const modulePath = require.resolve("expo/config-plugins", {
    paths: [process.cwd(), __dirname],
  });
  return require(modulePath);
}

function applyPermission(infoPlist, key, configuredValue, defaultValue) {
  if (configuredValue === false) {
    return;
  }

  if (typeof configuredValue === "string" && configuredValue.trim()) {
    infoPlist[key] = configuredValue;
    return;
  }

  if (typeof infoPlist[key] !== "string" || !infoPlist[key].trim()) {
    infoPlist[key] = defaultValue;
  }
}

module.exports = withAnsightReactNative;
module.exports.default = withAnsightReactNative;
module.exports.DEFAULT_CAMERA_PERMISSION = DEFAULT_CAMERA_PERMISSION;
module.exports.DEFAULT_LOCAL_NETWORK_PERMISSION = DEFAULT_LOCAL_NETWORK_PERMISSION;
