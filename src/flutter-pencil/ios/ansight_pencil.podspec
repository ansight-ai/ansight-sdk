Pod::Spec.new do |s|
  s.name = 'ansight_pencil'
  s.version = '1.9.0'
  s.summary = 'Optional Ansight Pencil simulation for Flutter on iOS Simulator.'
  s.homepage = 'https://github.com/ansight-ai/ansight-sdk'
  s.license = { :type => 'PolyForm Shield 1.0.0', :file => '../LICENSE' }
  s.author = { 'Ansight' => 'dev@ansight.ai' }
  s.source = { :path => '.' }
  s.source_files = 'ansight_pencil/Sources/ansight_pencil/**/*.swift'
  s.dependency 'Flutter'
  s.dependency 'AnsightPencil', s.version.to_s
  s.ios.deployment_target = '15.0'
  s.swift_version = '5.9'
  s.pod_target_xcconfig = { 'DEFINES_MODULE' => 'YES' }
end
