Pod::Spec.new do |s|
  s.name         = "AnsightToolsFileDescriptorDiagnostics"
  s.version      = "1.3.0-preview.9"
  s.summary      = "Ansight file descriptor diagnostic remote tools for native iOS apps"
  s.homepage     = "https://github.com/ansight-ai/ansight-sdk"
  s.license      = { :type => "Ansight SDK Source-Available License", :file => "LICENSE" }
  s.authors      = { "Ansight" => "dev@ansight.ai" }
  s.source       = { :path => "." }
  s.platforms    = { :ios => "15.0" }
  s.source_files = [
    "Sources/AnsightToolsFileDescriptorDiagnostics/**/*.swift",
    "Sources/CAnsightFileDescriptorDiagnostics/**/*.{c,h}"
  ]
  s.public_header_files = "Sources/CAnsightFileDescriptorDiagnostics/include/*.h"
  s.header_mappings_dir = "Sources/CAnsightFileDescriptorDiagnostics/include"
  s.dependency "AnsightCore", s.version.to_s
  s.swift_version = "6.0"
end
