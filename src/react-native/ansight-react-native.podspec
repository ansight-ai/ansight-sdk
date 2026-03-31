Pod::Spec.new do |s|
  s.name         = "AnsightReactNative"
  s.version      = "0.1.0-pre1"
  s.summary      = "React Native bridge for the Ansight native runtimes"
  s.homepage     = "https://github.com/ansight-ai/ansight-sdk"
  s.license      = { :type => "MIT" }
  s.authors      = { "Ansight" => "dev@ansight.ai" }
  s.source       = { :path => "." }
  s.platforms    = { :ios => "15.0" }
  s.source_files = "ios/*.{h,m,mm,swift}"
  s.dependency "React-Core"
  s.dependency "AnsightKit"
  s.swift_version = "6.0"
end
