Pod::Spec.new do |s|
  s.name         = "AnsightPairingQR"
  s.version      = "1.6.0"
  s.summary      = "Ansight file and QR enrollment UI for native iOS apps"
  s.homepage     = "https://github.com/ansight-ai/ansight-sdk"
  s.license      = { :type => "Ansight SDK Source-Available License", :file => "LICENSE" }
  s.authors      = { "Ansight" => "dev@ansight.ai" }
  s.source       = { :path => "." }
  s.platforms    = { :ios => "15.0", :osx => "10.15" }
  s.source_files = "Sources/AnsightPairingQR/**/*.swift"
  s.frameworks   = "AVFoundation", "UniformTypeIdentifiers"
  s.ios.frameworks = "UIKit"
  s.dependency "AnsightCore", s.version.to_s
  s.swift_version = "6.0"
end
