#####################################################
#   EC2 INSTANCE DETAILS
#####################################################
output "instance_ids" {
  description = "Map of ECS container instance IDs keyed by group name"
  value       = { for name, inst in aws_instance.ecs_instance : name => inst.id }
}

output "instance_public_ips" {
  description = "Map of public IPv4 addresses (if associated) keyed by group name"
  value       = { for name, inst in aws_instance.ecs_instance : name => inst.public_ip }
}

output "instance_private_ips" {
  description = "Map of private IPv4 addresses keyed by group name"
  value       = { for name, inst in aws_instance.ecs_instance : name => inst.private_ip }
}

output "instance_public_dns" {
  description = "Map of public DNS names (if public IP associated) keyed by group name"
  value       = { for name, inst in aws_instance.ecs_instance : name => inst.public_dns }
}

output "elastic_ips" {
  description = "Map of Elastic IPs attached to instances (only for groups with associate_public_ip = true)"
  value       = { for name, eip in aws_eip.ec2_eip : name => eip.public_ip }
}

#####################################################
#   ECS CLUSTER DETAILS
#####################################################
output "ecs_cluster_id" {
  description = "ID of the ECS cluster"
  value       = aws_ecs_cluster.main.id
}

output "ecs_cluster_name" {
  description = "Name of the ECS cluster"
  value       = aws_ecs_cluster.main.name
}

output "ecs_cluster_arn" {
  description = "ARN of the ECS cluster"
  value       = aws_ecs_cluster.main.arn
}

#####################################################
#   IAM / ACCESS
#####################################################
output "iam_role_arn" {
  description = "ARN of the IAM role attached to the instances"
  value       = aws_iam_role.ec2_role.arn
}

output "iam_instance_profile_name" {
  description = "Name of the IAM instance profile"
  value       = aws_iam_instance_profile.ec2_profile.name
}

output "key_pair_name" {
  description = "Key-pair name used for SSH"
  value       = aws_key_pair.ec2_key.key_name
}

#####################################################
#   AMI & KEY MATERIAL
#####################################################
output "ami_id" {
  description = "AMI ID of the Amazon Linux 2 ECS-optimised image"
  value       = data.aws_ami.ecs_optimized.id
}

output "ec2_private_key_pem" {
  description = "Private key material for the generated key pair"
  value       = tls_private_key.ec2_key.private_key_pem
  sensitive   = true
}