Pod::Spec.new do |s|
  s.name         = "AnsightCore"
  s.version      = "1.3.0-preview.3"
  s.summary      = "Native iOS runtime for Ansight"
  s.homepage     = "https://github.com/ansight-ai/ansight-sdk"
  s.license      = { :type => "Ansight SDK Source-Available License", :file => "LICENSE" }
  s.authors      = { "Ansight" => "dev@ansight.ai" }
  s.source       = { :path => "." }
  s.platforms    = { :ios => "15.0" }
  s.source_files = "Sources/AnsightCore/**/*.swift", "Sources/CAnsightCrashCapture/**/*.{c,h}", "Generated/CocoaPods/AnsightGeneratedBuildArtifacts.swift"
  s.public_header_files = "Sources/CAnsightCrashCapture/include/*.h"
  s.preserve_paths = "Plugins/AnsightBuildTool/**/*.swift"
  s.frameworks   = "CryptoKit", "Metal", "Network", "QuartzCore", "Security", "UIKit"
  s.script_phase = {
    :name => "Generate Ansight Build Artifacts",
    :execution_position => :before_compile,
    :output_files => ["${PODS_TARGET_SRCROOT}/Generated/CocoaPods/AnsightGeneratedBuildArtifacts.swift"],
    :script => <<-SCRIPT
set -euo pipefail

TOOL="${DERIVED_FILE_DIR}/ansight-build-tool"
mkdir -p "$(dirname "${TOOL}")" "${PODS_TARGET_SRCROOT}/Generated/CocoaPods"
MACOS_SDK="$(xcrun --sdk macosx --show-sdk-path)"
SDKROOT="${MACOS_SDK}" xcrun --sdk macosx swiftc -sdk "${MACOS_SDK}" "${PODS_TARGET_SRCROOT}/Plugins/AnsightBuildTool/"*.swift -o "${TOOL}"
"${TOOL}" \
  --output-file "${PODS_TARGET_SRCROOT}/Generated/CocoaPods/AnsightGeneratedBuildArtifacts.swift" \
  --target-directory "${PODS_TARGET_SRCROOT}/Sources/AnsightCore"
    SCRIPT
  }
  s.swift_version = "6.0"
end
