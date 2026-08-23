#
# To learn more about a Podspec see http://guides.cocoapods.org/syntax/podspec.html.
# Run `pod lib lint ansight.podspec` to validate before publishing.
#
Pod::Spec.new do |s|
  s.name             = 'ansight_flutter'
  s.version          = '1.3.0-preview.11'
  s.summary          = 'Flutter bridge for the Ansight mobile observability SDK.'
  s.description      = <<-DESC
Cross-platform observability, inspection, and remote tooling for Flutter apps.
                       DESC
  s.homepage         = 'https://github.com/ansight-ai/ansight-sdk'
  s.license          = { :type => 'Ansight SDK Source-Available License', :file => '../LICENSE' }
  s.author           = { 'Ansight' => 'dev@ansight.ai' }
  s.source           = { :path => '.' }
  s.source_files = 'ansight_flutter/Sources/ansight_flutter/**/*'
  s.dependency 'Flutter'
  s.dependency 'Ansight', s.version.to_s
  s.platform = :ios, '15.0'

  # Flutter.framework does not contain a i386 slice.
  s.pod_target_xcconfig = { 'DEFINES_MODULE' => 'YES', 'EXCLUDED_ARCHS[sdk=iphonesimulator*]' => 'i386' }
  s.swift_version = '5.9'

  # If your plugin requires a privacy manifest, for example if it uses any
  # required reason APIs, update the PrivacyInfo.xcprivacy file to describe your
  # plugin's privacy impact, and then uncomment this line. For more information,
  # see https://developer.apple.com/documentation/bundleresources/privacy_manifest_files
  s.resource_bundles = {'ansight_flutter_privacy' => ['ansight_flutter/Sources/ansight_flutter/PrivacyInfo.xcprivacy']}
end
