Pod::Spec.new do |s|
  s.name         = "AnsightKit"
  s.version      = "0.1.0-pre1"
  s.summary      = "Native iOS runtime for Ansight"
  s.homepage     = "https://github.com/ansight-ai/ansight-sdk"
  s.license      = { :type => "MIT" }
  s.authors      = { "Ansight" => "dev@ansight.ai" }
  s.source       = { :path => "." }
  s.platforms    = { :ios => "15.0" }
  s.source_files = "Sources/AnsightKit/**/*.swift"
  s.swift_version = "6.0"
end
