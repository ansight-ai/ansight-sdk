Pod::Spec.new do |s|
  s.name         = "AnsightObjC"
  s.version      = "1.3.0-preview.5"
  s.summary      = "Objective-C facade for the Ansight native iOS SDK"
  s.homepage     = "https://github.com/ansight-ai/ansight-sdk"
  s.license      = { :type => "Ansight SDK Source-Available License", :file => "LICENSE" }
  s.authors      = { "Ansight" => "dev@ansight.ai" }
  s.source       = { :path => "." }
  s.platforms    = { :ios => "15.0" }
  s.source_files = "Sources/AnsightObjC/**/*.swift"
  s.dependency "Ansight", s.version.to_s
  s.swift_version = "6.0"
end
