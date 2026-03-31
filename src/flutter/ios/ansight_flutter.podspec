Pod::Spec.new do |s|
  s.name             = "ansight_flutter"
  s.version          = "0.1.0-pre1"
  s.summary          = "Flutter bridge for the Ansight native runtimes"
  s.homepage         = "https://github.com/ansight-ai/ansight-sdk"
  s.license          = { :type => "MIT" }
  s.authors          = { "Ansight" => "dev@ansight.ai" }
  s.source           = { :path => "." }
  s.source_files     = "Classes/**/*.{h,m,mm,swift}"
  s.platform         = :ios, "15.0"
  s.swift_version    = "6.0"
  s.dependency "Flutter"
  s.dependency "AnsightKit"
  s.pod_target_xcconfig = {
    "DEFINES_MODULE" => "YES"
  }
end
