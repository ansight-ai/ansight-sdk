Pod::Spec.new do |s|
  s.name         = "AnsightLocation"
  s.version      = "1.3.0-preview.4"
  s.summary      = "Explicit observed-location recording for existing Ansight sessions"
  s.homepage     = "https://github.com/ansight-ai/ansight-sdk"
  s.license      = { :type => "Ansight SDK Source-Available License", :file => "LICENSE" }
  s.authors      = { "Ansight" => "dev@ansight.ai" }
  s.source       = { :path => "." }
  s.platforms    = { :ios => "15.0" }
  s.source_files = "Sources/AnsightLocation/**/*.swift"
  s.dependency "AnsightCore", s.version.to_s
  s.swift_version = "6.0"
end
