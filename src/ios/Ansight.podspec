Pod::Spec.new do |s|
  s.name         = "Ansight"
  s.version      = "1.4.0-preview.1"
  s.summary      = "Aggregate Ansight native iOS SDK with developer defaults and remote-tool suites"
  s.homepage     = "https://github.com/ansight-ai/ansight-sdk"
  s.license      = { :type => "Ansight SDK Source-Available License", :file => "LICENSE" }
  s.authors      = { "Ansight" => "dev@ansight.ai" }
  s.source       = { :path => "." }
  s.platforms    = { :ios => "15.0" }
  s.source_files = "Sources/Ansight/**/*.swift"
  s.dependency "AnsightCore", s.version.to_s
  s.dependency "AnsightPairingQR", s.version.to_s
  s.dependency "AnsightToolsDatabase", s.version.to_s
  s.dependency "AnsightToolsFileDescriptorDiagnostics", s.version.to_s
  s.dependency "AnsightToolsFileSystem", s.version.to_s
  s.dependency "AnsightToolsPreferences", s.version.to_s
  s.dependency "AnsightToolsReflection", s.version.to_s
  s.dependency "AnsightToolsSecureStorage", s.version.to_s
  s.dependency "AnsightToolsVisualTree", s.version.to_s
  s.swift_version = "6.0"
end
