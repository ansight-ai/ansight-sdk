Pod::Spec.new do |s|
  s.name             = 'ansight_flutter'
  s.version          = '1.6.1'
  s.summary          = 'Flutter bridge for the Ansight macOS observability SDK.'
  s.description      = <<-DESC
Cross-platform observability, inspection, and remote tooling for Flutter apps.
                       DESC
  s.homepage         = 'https://github.com/ansight-ai/ansight-sdk'
  s.license          = { :type => 'Ansight SDK Source-Available License', :file => '../LICENSE' }
  s.author           = { 'Ansight' => 'dev@ansight.ai' }
  s.source           = { :path => '.' }
  s.source_files     = 'ansight_flutter/Sources/ansight_flutter/**/*'
  s.dependency 'FlutterMacOS'
  s.dependency 'Ansight', s.version.to_s
  s.platform = :osx, '10.15'
  s.pod_target_xcconfig = { 'DEFINES_MODULE' => 'YES' }
  s.swift_version = '5.9'
  s.resource_bundles = {
    'ansight_flutter_privacy' => [
      'ansight_flutter/Sources/ansight_flutter/PrivacyInfo.xcprivacy'
    ]
  }
end
