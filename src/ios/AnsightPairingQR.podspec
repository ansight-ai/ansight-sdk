Pod::Spec.new do |s|
  s.name         = "AnsightPairingQR"
  s.version      = "1.2.0-preview.2"
  s.summary      = "Ansight file and QR pairing UI for native iOS apps"
  s.homepage     = "https://github.com/ansight-ai/ansight-sdk"
  s.license      = { :type => "Ansight SDK Source-Available License", :file => "LICENSE" }
  s.authors      = { "Ansight" => "dev@ansight.ai" }
  s.source       = { :path => "." }
  s.platforms    = { :ios => "15.0" }
  s.source_files = "Sources/AnsightPairingQR/**/*.swift"
  s.frameworks   = "AVFoundation", "UIKit", "UniformTypeIdentifiers"
  s.dependency "AnsightCore", s.version.to_s
  s.swift_version = "6.0"
end
