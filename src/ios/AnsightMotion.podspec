Pod::Spec.new do |s|
  s.name         = "AnsightMotion"
  s.version      = "1.7.1"
  s.summary      = "App-fed motion capture for Ansight iOS apps"
  s.homepage     = "https://github.com/ansight-ai/ansight-sdk"
  s.license      = { :type => "PolyForm Shield 1.0.0", :file => "LICENSE" }
  s.authors      = { "Ansight" => "dev@ansight.ai" }
  s.source       = { :path => "." }
  s.platforms    = { :ios => "15.0", :osx => "10.15" }
  s.source_files = "Sources/AnsightMotion/**/*.swift"
  s.dependency "AnsightCore", s.version.to_s
  s.swift_version = "6.0"
end
