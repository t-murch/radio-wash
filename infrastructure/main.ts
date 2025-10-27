import { Construct } from 'constructs';
import { App, TerraformStack } from 'cdktf';
import { AzurermProvider } from './.gen/providers/azurerm/provider';
import { DataAzurermResourceGroup } from './.gen/providers/azurerm/data-azurerm-resource-group';
import { ConsumptionBudgetResourceGroup } from './.gen/providers/azurerm/consumption-budget-resource-group';
import { MonitorActionGroup } from './.gen/providers/azurerm/monitor-action-group';

class RadioWashInfrastructure extends TerraformStack {
  constructor(scope: Construct, id: string) {
    super(scope, id);

    // Configure Azure Provider
    new AzurermProvider(this, 'AzureRM', {
      features: {},
    });

    // Reference existing Resource Group
    const existingResourceGroup = new DataAzurermResourceGroup(
      this,
      'ExistingResourceGroup',
      {
        name: 'radio-wash_group',
      }
    );

    // Get admin email from environment variable
    const adminEmail = process.env.ADMIN_EMAIL;
    if (!adminEmail) {
      throw new Error('ADMIN_EMAIL environment variable is required');
    }

    // Action Group for budget alerts (email notifications)
    const budgetActionGroup = new MonitorActionGroup(this, 'BudgetAlerts', {
      name: 'radiowash-budget-alerts',
      resourceGroupName: existingResourceGroup.name,
      shortName: 'budget',
      emailReceiver: [
        {
          name: 'admin',
          emailAddress: adminEmail,
        },
      ],
    });

    // Budget Alert - $50/month limit
    new ConsumptionBudgetResourceGroup(this, 'RadioWashBudget', {
      name: 'radiowash-monthly-budget',
      resourceGroupId: existingResourceGroup.id,
      amount: 50,
      timeGrain: 'Monthly',
      timePeriod: {
        startDate: '2025-10-01T00:00:00Z',
        endDate: '2025-12-31T23:59:59Z',
      },
      notification: [
        {
          enabled: true,
          threshold: 80, // Alert at 80% of budget ($32)
          operator: 'GreaterThan',
          contactEmails: [adminEmail],
          contactGroups: [budgetActionGroup.id],
        },
        {
          enabled: true,
          threshold: 100, // Alert at 100% of budget ($50)
          operator: 'GreaterThan',
          contactEmails: [adminEmail],
          contactGroups: [budgetActionGroup.id],
        },
      ],
    });
  }
}

const app = new App();
new RadioWashInfrastructure(app, 'radiowash-infrastructure');
app.synth();
