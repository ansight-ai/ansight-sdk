Pod::Spec.new do |s|
  s.name         = "AnsightPencilRuntime"
  s.version      = "1.9.0"
  s.summary      = "Optional Pencil input simulation for iOS Simulator"
  s.homepage     = "https://github.com/ansight-ai/ansight-sdk"
  s.license      = { :type => "PolyForm Shield 1.0.0", :file => "LICENSE" }
  s.authors      = { "Ansight" => "dev@ansight.ai" }
  s.source       = { :path => "." }
  s.platforms    = { :ios => "15.0", :osx => "10.15" }
  s.source_files = "Sources/AnsightPencilRuntime/**/*.swift"
  s.ios.frameworks = "UIKit"
  s.swift_version = "6.0"
end
